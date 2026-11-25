#!/usr/bin/env python3
"""Fail when an IAM role under the /cmms/ path doesn't carry the cmms permissions boundary.

Usage: python scripts/check-boundaries.py <template.yml> [<template.yml> ...]

The CloudFormation execution role (deploy/aws/bootstrap.yml) only creates roles under /cmms/ and only with the
cmms-boundary policy attached, so a /cmms/ role without it would fail on deploy. This check finds it in CI instead.
Templates are read with cfn-lint's decoder so short-form tags (!Ref, !Sub, ...) parse.

A role whose Path starts with /cmms/ (or is built with an intrinsic function, so can't be read) must have
  PermissionsBoundary: !Ref BoundaryPolicyArn     (a parameter of that name: the bootstrap stack's output), or,
  PermissionsBoundary: !Ref <policy>              where <policy> is the cmms-boundary AWS::IAM::ManagedPolicy defined in
                                                  the same template (only bootstrap.yml defines it, so a role there
                                                  would point at it directly rather than through a parameter).
Anything else (no boundary, !Ref AWS::NoValue, an ARN string, !If, another policy) fails: the execution role accepts
exactly the bootstrap's boundary ARN, so a different value would also fail on deploy.

Exit codes: 0 all good; 1 a role fails (or a template can't be read); 2 bad usage.
A role with no Path is created at "/" (not /cmms/); it is listed as a warning only.
"""
import sys

from cfnlint.decode import cfn_yaml

CMMS_PATH = "/cmms/"
BOUNDARY_PARAMETER = "BoundaryPolicyArn"
BOUNDARY_POLICY_NAME = "cmms-boundary"


def is_cmms_boundary_policy(resource):
    """True for the cmms-boundary managed policy itself (ManagedPolicyName "cmms-boundary..." as a string or !Sub)."""
    if not isinstance(resource, dict) or resource.get("Type") != "AWS::IAM::ManagedPolicy":
        return False
    name = (resource.get("Properties") or {}).get("ManagedPolicyName")
    if isinstance(name, dict) and "Fn::Sub" in name:
        name = name["Fn::Sub"] if isinstance(name["Fn::Sub"], str) else name["Fn::Sub"][0]
    return isinstance(name, str) and name.startswith(BOUNDARY_POLICY_NAME)


def boundary_problem(boundary, template):
    """None when the boundary is the cmms one, otherwise what is wrong with it."""
    if boundary is None:
        return "has no PermissionsBoundary"
    target = boundary.get("Ref") if isinstance(boundary, dict) and set(boundary) == {"Ref"} else None
    if target == BOUNDARY_PARAMETER and BOUNDARY_PARAMETER in (template.get("Parameters") or {}):
        return None
    if target is not None and is_cmms_boundary_policy((template.get("Resources") or {}).get(target)):
        return None
    if target == "AWS::NoValue":
        return "has no PermissionsBoundary (!Ref AWS::NoValue)"
    if target == BOUNDARY_PARAMETER:
        return f"refers to {BOUNDARY_PARAMETER}, which isn't a parameter of this template"
    return f"has PermissionsBoundary {boundary!r}, not !Ref {BOUNDARY_PARAMETER}"


def is_cmms_path(role_path):
    # A Path built with an intrinsic function can't be read here; treat it as /cmms/ to be safe.
    return not isinstance(role_path, str) or role_path.startswith(CMMS_PATH)


def check(path):
    """Returns (failures, warnings) as lists of messages for one template."""
    template = cfn_yaml.load(path)
    resources = template.get("Resources") or {}
    failures, warnings = [], []
    for logical_id, resource in resources.items():
        if not isinstance(resource, dict) or resource.get("Type") != "AWS::IAM::Role":
            continue
        properties = resource.get("Properties") or {}
        role_path = properties.get("Path")
        if role_path is None:
            warnings.append(f"{path}: {logical_id} has no Path (created at /, not {CMMS_PATH}); not checked")
            continue
        if not is_cmms_path(role_path):
            continue
        problem = boundary_problem(properties.get("PermissionsBoundary"), template)
        if problem:
            shown = role_path if isinstance(role_path, str) else "not a plain string"
            failures.append(f"MISSING BOUNDARY {path}: {logical_id} (Path {shown}) {problem}")
    return failures, warnings


def main(argv):
    if len(argv) < 2:
        print(__doc__.strip().splitlines()[2], file=sys.stderr)
        return 2
    failures, warnings = [], []
    for path in argv[1:]:
        try:
            template_failures, template_warnings = check(path)
        except Exception as error:  # a template that can't be read must not pass silently
            failures.append(f"UNREADABLE {path}: {type(error).__name__}: {error}")
            continue
        failures += template_failures
        warnings += template_warnings
    for message in warnings:
        print(f"WARNING {message}")
    for message in failures:
        print(message)
    if failures:
        print(f"check-boundaries: {len(failures)} problem(s); every role under {CMMS_PATH} needs PermissionsBoundary: !Ref {BOUNDARY_PARAMETER}")
        return 1
    print(f"BOUNDARIES-OK ({len(argv) - 1} template(s))")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
