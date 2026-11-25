#!/usr/bin/env python3
"""Fail when an IAM role under the /cmms/ path has no permissions boundary.

Usage: python scripts/check-boundaries.py <template.yml> [<template.yml> ...]

The CloudFormation execution role (deploy/aws/bootstrap.yml) only creates roles under /cmms/ and only with the
cmms-boundary policy attached, so a /cmms/ role without PermissionsBoundary would fail on deploy. This check finds it in CI
instead. Templates are read with cfn-lint's decoder so short-form tags (!Ref, !Sub, ...) parse.

Exit codes: 0 every /cmms/ role has a boundary; 1 at least one doesn't (or a template can't be read); 2 bad usage.
A role with no Path is created at "/" (not /cmms/); it is listed as a warning only.
"""
import sys

from cfnlint.decode import cfn_yaml

CMMS_PATH = "/cmms/"


def has_boundary(properties):
    boundary = properties.get("PermissionsBoundary")
    if boundary is None:
        return False
    # PermissionsBoundary: !Ref AWS::NoValue is the same as leaving it out.
    return not (isinstance(boundary, dict) and boundary.get("Ref") == "AWS::NoValue")


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
        if has_boundary(properties):
            continue
        if role_path == CMMS_PATH:
            failures.append(f"MISSING BOUNDARY {path}: {logical_id} (Path {CMMS_PATH}) has no PermissionsBoundary")
        elif not isinstance(role_path, str):
            # A Path built with an intrinsic function can't be read here; treat it as /cmms/ to be safe.
            failures.append(f"MISSING BOUNDARY {path}: {logical_id} (Path not a plain string) has no PermissionsBoundary")
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
        print(f"check-boundaries: {len(failures)} problem(s); every role under {CMMS_PATH} needs PermissionsBoundary")
        return 1
    print(f"BOUNDARIES-OK ({len(argv) - 1} template(s))")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
