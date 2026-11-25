#!/usr/bin/env bash
# One-time setup that lets this repository's GitHub Actions deploy CMX into your AWS account.
#
# Run it in AWS CloudShell, in the region where the app should run (region menu at the top right):
#   curl -fsSLO https://raw.githubusercontent.com/MoXFahmy/CMMSX/main/deploy/aws/setup.sh
#   bash setup.sh
#
# Optional settings (environment variables):
#   REGION=me-central-1     AWS region (default: CloudShell's current region)
#   REPO=MoXFahmy/CMMSX     GitHub repository allowed to deploy (from its main branch)
#
# It creates two CloudFormation stacks and changes nothing else in the account: "cmms-github" in the chosen region (the
# GitHub deploy role, image repository, permissions boundary, backup guard and CloudTrail trail) and "cmms-dr" in eu-west-1
# (the locked backup copies in Ireland, which the deploy pipeline may read but never change).
# It is safe to run again, and it is how a change to deploy/aws/bootstrap.yml or infra/aws/dr.yml reaches AWS.
set -euo pipefail

REPO="${REPO:-MoXFahmy/CMMSX}"
REGION="${REGION:-${AWS_REGION:-$(aws configure get region 2>/dev/null || true)}}"
STACK="${STACK:-cmms-github}"
BRANCH="${BRANCH:-main}"
if [ -z "$REGION" ]; then
  echo "Choose a region, e.g.:  REGION=me-central-1 bash setup.sh" >&2
  exit 1
fi
export AWS_REGION="$REGION"

ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
echo "AWS account: $ACCOUNT"
echo "Region:      $REGION"
echo "Repository:  $REPO"
echo

TEMPLATE="$(dirname "$0")/bootstrap.yml"
if [ ! -f "$TEMPLATE" ]; then
  TEMPLATE=$(mktemp --suffix .yml)
  curl -fsSL "https://raw.githubusercontent.com/$REPO/$BRANCH/deploy/aws/bootstrap.yml" -o "$TEMPLATE"
fi

# An account has at most one GitHub identity provider. Keep the choice made on an earlier run of this stack;
# otherwise reuse a provider that already exists (e.g. created for another project or region).
CREATE_PROVIDER=$(aws cloudformation describe-stacks --stack-name "$STACK" \
  --query "Stacks[0].Parameters[?ParameterKey=='CreateOidcProvider'].ParameterValue" --output text 2>/dev/null || true)
if [ "$CREATE_PROVIDER" != "true" ] && [ "$CREATE_PROVIDER" != "false" ]; then
  if aws iam get-open-id-connect-provider \
       --open-id-connect-provider-arn "arn:aws:iam::${ACCOUNT}:oidc-provider/token.actions.githubusercontent.com" >/dev/null 2>&1; then
    CREATE_PROVIDER=false
  else
    CREATE_PROVIDER=true
  fi
fi

# One multi-region CloudTrail trail is enough (a second one of the same events costs $2 per 100,000 events). Keep the
# choice made on an earlier run; otherwise create cmms-trail unless another multi-region trail is already logging.
# describe-trails also lists multi-region trails set up in other regions. A cmms-trail set up in this region is this
# stack's own (never "another" trail). A cmms-trail from another region (this setup run there first, e.g. before a
# rebuild here) counts even if its region is down and can't report logging: its bucket name is taken account-wide.
CREATE_TRAIL=$(aws cloudformation describe-stacks --stack-name "$STACK" \
  --query "Stacks[0].Parameters[?ParameterKey=='CreateTrail'].ParameterValue" --output text 2>/dev/null || true)
if [ "$CREATE_TRAIL" != "true" ] && [ "$CREATE_TRAIL" != "false" ]; then
  CREATE_TRAIL=true
  while IFS=$'\t' read -r TRAIL_NAME TRAIL_ARN TRAIL_HOME; do
    [ -n "$TRAIL_ARN" ] || continue
    if [ "$TRAIL_NAME" = cmms-trail ]; then
      echo "Found CMX's CloudTrail trail $TRAIL_ARN (set up in $TRAIL_HOME): using it instead of creating another."
      CREATE_TRAIL=false
      break
    fi
    if [ "$(aws cloudtrail get-trail-status --name "$TRAIL_ARN" --query IsLogging --output text 2>/dev/null || true)" = "True" ]; then
      echo "Found CloudTrail trail $TRAIL_ARN (multi-region, logging): using it instead of creating cmms-trail."
      CREATE_TRAIL=false
      break
    fi
  done <<< "$(aws cloudtrail describe-trails \
    --query "trailList[?IsMultiRegionTrail && !(Name=='cmms-trail' && HomeRegion=='$REGION')].[Name, TrailARN, HomeRegion]" \
    --output text 2>/dev/null || true)"
fi

# GitHub signs its tokens with a subject that includes the owner's and repository's numeric ids
# (repo:owner@123/name@456:ref:refs/heads/main); look them up so the role accepts exactly this repository.
IDS=$(curl -fsSL "https://api.github.com/repos/$REPO" 2>/dev/null | jq -r '"\(.owner.id) \(.id)"' 2>/dev/null || true)
OWNER_ID=${IDS%% *}
REPO_ID=${IDS##* }
if [[ ! "$OWNER_ID" =~ ^[0-9]+$ || ! "$REPO_ID" =~ ^[0-9]+$ ]]; then
  echo "Note: could not look up $REPO on api.github.com (private repository?); only the name-only token subject will be accepted." >&2
  OWNER_ID=""
  REPO_ID=""
fi

echo "Creating the GitHub deployment role and image repository (stack $STACK, about 1 minute)..."
aws cloudformation deploy --stack-name "$STACK" --template-file "$TEMPLATE" \
  --capabilities CAPABILITY_NAMED_IAM --no-fail-on-empty-changeset \
  --tags application=4c-cmms \
  --parameter-overrides GitHubRepo="$REPO" CreateOidcProvider="$CREATE_PROVIDER" GitHubOwnerId="$OWNER_ID" GitHubRepoId="$REPO_ID" \
    CreateTrail="$CREATE_TRAIL"

ROLE_ARN=$(aws cloudformation describe-stacks --stack-name "$STACK" \
  --query "Stacks[0].Outputs[?OutputKey=='DeployRoleArn'].OutputValue" --output text)

# The recovery stack in Ireland: the locked backup vault, its key and the two Object Lock buckets that receive the copies.
# Only this script deploys it, as the account administrator: the deploy pipeline's roles may not create, change or delete
# anything in it (backup-guard), and the workflow only reads its outputs. Always eu-west-1, wherever the app runs.
DR_STACK=cmms-dr
DR_REGION=eu-west-1
DR_TEMPLATE="$(dirname "$0")/../../infra/aws/dr.yml"
if [ ! -f "$DR_TEMPLATE" ]; then
  DR_TEMPLATE=$(mktemp --suffix .yml)
  curl -fsSL "https://raw.githubusercontent.com/$REPO/$BRANCH/infra/aws/dr.yml" -o "$DR_TEMPLATE"
fi
DR_STATUS=$(aws cloudformation describe-stacks --region "$DR_REGION" --stack-name "$DR_STACK" \
  --query "Stacks[0].StackStatus" --output text 2>/dev/null || echo NONE)
case "$DR_STATUS" in
  ROLLBACK_COMPLETE)
    # Its first create failed and was rolled back: CloudFormation removed what it had made (nothing had been copied into
    # it yet), so the empty stack is removed and created again.
    echo "Removing the failed first attempt at the recovery stack $DR_STACK ($DR_REGION)..."
    aws cloudformation delete-stack --region "$DR_REGION" --stack-name "$DR_STACK"
    aws cloudformation wait stack-delete-complete --region "$DR_REGION" --stack-name "$DR_STACK"
    ;;
  REVIEW_IN_PROGRESS) ;; # a change set that was never applied: deploy creates the stack as usual
  *_IN_PROGRESS)
    echo "The recovery stack $DR_STACK ($DR_REGION) is busy ($DR_STATUS). Wait until it finishes, then run this script again." >&2
    exit 1
    ;;
  *_FAILED)
    echo "The recovery stack $DR_STACK ($DR_REGION) is in $DR_STATUS. Open it in the CloudFormation console (region Ireland)," >&2
    echo "read its failed events and fix the cause (see docs/SECURITY_AND_BACKUPS.md, section 7), then run this script again." >&2
    exit 1
    ;;
esac
echo "Creating or updating the recovery stack $DR_STACK in $DR_REGION (locked backup copies; a few minutes the first time)..."
aws cloudformation deploy --region "$DR_REGION" --stack-name "$DR_STACK" --template-file "$DR_TEMPLATE" \
  --no-fail-on-empty-changeset --tags application=4c-cmms
echo "The recovery stack's outputs (the deploy workflow reads them; nothing to copy):"
aws cloudformation describe-stacks --region "$DR_REGION" --stack-name "$DR_STACK" \
  --query "Stacks[0].Outputs[].[OutputKey, OutputValue]" --output table

cat <<'EOF'

This version of the setup (CMX 0.29.0) adds:
  - a permissions boundary (IAM policy cmms-boundary-<region>) that every role the app creates must carry;
  - a guard that stops the deploy pipeline from deleting backups or the CloudTrail logs, and from changing anything in
    the locked copies in Ireland (only your own console administrator can lift the Ireland lock);
  - the recovery stack cmms-dr in eu-west-1 (locked backup vault, its key and the two copy buckets), deployed by this
    script and never by GitHub Actions: after a change to infra/aws/dr.yml, run this script again;
  - permission for GitHub Actions to deploy one more stack, cmms-edge in us-east-1 (web firewall), and to read cmms-dr;
  - GitHub Actions sign-ins that last up to 4 hours (was 2), so a deploy can wait up to 3 hours for a long app update;
  - a bucket for the stacks' templates (output TemplateBucketName; old templates expire after 30 days);
  - a CloudTrail trail, cmms-trail, recording management events from every region into a locked bucket
    (output TrailBucketName), unless the account already had a multi-region trail that is logging.
EOF

if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
  gh variable set AWS_REGION --repo "$REPO" --body "$REGION"
  gh variable set AWS_DEPLOY_ROLE_ARN --repo "$REPO" --body "$ROLE_ARN"
  echo
  echo "Done. GitHub repository variables set. Start the first deployment here (Run workflow):"
else
  cat <<EOF

Done. In GitHub, add these two repository variables:
  https://github.com/$REPO/settings/variables/actions  ->  New repository variable

  Name                  Value
  AWS_REGION            $REGION
  AWS_DEPLOY_ROLE_ARN   $ROLE_ARN

Then start the first deployment here (Run workflow):
EOF
fi
echo "  https://github.com/$REPO/actions/workflows/deploy-aws.yml"
