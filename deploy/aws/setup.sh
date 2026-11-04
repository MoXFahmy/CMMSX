#!/usr/bin/env bash
# One-time setup that lets this repository's GitHub Actions deploy 4C CMMS into your AWS account.
#
# Run it in AWS CloudShell, in the region where the app should run (region menu at the top right):
#   curl -fsSLO https://raw.githubusercontent.com/MoXFahmy/CMMSX/main/deploy/aws/setup.sh
#   bash setup.sh
#
# Optional settings (environment variables):
#   REGION=me-central-1     AWS region (default: CloudShell's current region)
#   REPO=MoXFahmy/CMMSX     GitHub repository allowed to deploy (from its main branch)
#
# It creates one CloudFormation stack, "cmms-github", and changes nothing else in the account.
# It is safe to run again.
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

echo "Creating the GitHub deployment role and image repository (stack $STACK, about 1 minute)..."
aws cloudformation deploy --stack-name "$STACK" --template-file "$TEMPLATE" \
  --capabilities CAPABILITY_NAMED_IAM --no-fail-on-empty-changeset \
  --tags application=4c-cmms \
  --parameter-overrides GitHubRepo="$REPO" CreateOidcProvider="$CREATE_PROVIDER"

ROLE_ARN=$(aws cloudformation describe-stacks --stack-name "$STACK" \
  --query "Stacks[0].Outputs[?OutputKey=='DeployRoleArn'].OutputValue" --output text)

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
