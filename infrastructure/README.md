# Infrastructure

AWS CDK in C# — VPC, RDS, S3, CloudFront, ECS Fargate, Secrets Manager, Route 53.

## Stacks

| Stack | Environments | Resources |
|---|---|---|
| `{env}-vpc` | both | VPC, subnets, security groups, ECS cluster |
| `{env}-storage` | both | RDS PostgreSQL, S3 video bucket, S3 app bucket, DB secret |
| `{env}-messaging` | both | RabbitMQ on Fargate, SES domain identity, RabbitMQ secret |
| `{env}-ecr` | both | ECR repositories for all three services |
| `{env}-cloudfront` | both | CloudFront distributions (app + video), OAC bucket policies; signing key group production only |
| `{env}-route53` | production only | Route 53 hosted zone, A alias records, SES DKIM CNAME records |

## Environment differences

| Feature | testing | production |
|---|---|---|
| CloudFront custom domains | no — uses `*.cloudfront.net` | yes — `app.` and `cdn.yourdomain.com` |
| ACM certificate | not required | required (`certArn` context) |
| Video signed URLs | not enforced | enforced via key group (`cfPublicKey` context) |
| Route 53 stack | not deployed | deployed |
| RDS Multi-AZ | no | yes |
| RDS deletion protection | no | yes |

## Prerequisites

- AWS CLI configured (`aws configure`) with credentials for the target account
- CDK CLI installed: `npm install -g aws-cdk`
- CDK bootstrapped in the target account/region (one-time):
  ```bash
  cdk bootstrap aws://ACCOUNT_ID/REGION
  ```

## Deploying — testing

No pre-deployment steps required.

```bash
cd infrastructure

cdk deploy --all \
  --context env=testing \
  --context domain=yourdomain.com
```

Video URLs in testing are plain CloudFront URLs — no signing enforced.

## Deploying — production

Complete these steps before the first production deploy.

### 1. Register or import your domain into Route 53

Domain registration creates a hosted zone. CDK creates its own hosted zone during deploy — if one already exists from registration, consider importing it to avoid a duplicate.

### 2. Create the ACM wildcard certificate

CloudFront requires a certificate in `us-east-1` regardless of deployment region.

```bash
aws acm request-certificate \
  --domain-name "*.yourdomain.com" \
  --validation-method DNS \
  --region us-east-1
```

Because the Route 53 hosted zone doesn't exist yet (CDK creates it on first deploy), use **email validation**, or manually create the zone first and add the CNAME validation record. Note the certificate ARN once validated.

### 3. Generate an RSA key pair for CloudFront signed URLs

```bash
openssl genrsa -out cf-private.pem 2048
openssl rsa -pubout -in cf-private.pem -out cf-public.pem
```

Store `cf-private.pem` in AWS Secrets Manager or Parameter Store — the messaging-service needs it at runtime to sign video URLs. The public key is passed to CDK at deploy time.

### 4. Deploy

```bash
cdk deploy --all \
  --context env=production \
  --context domain=yourdomain.com \
  --context certArn=arn:aws:acm:us-east-1:ACCOUNT_ID:certificate/CERT_ID \
  --context cfPublicKey="$(cat cf-public.pem)"
```

### 5. Post-deployment

- **Nameservers** — update your domain registrar with the Route 53 nameservers shown in the AWS console.
- **SES production access** — SES starts in sandbox mode. Request production access in the AWS console to send to unverified addresses.

## Running tests

```bash
dotnet test
```

No AWS credentials required — tests run entirely against synthesised CloudFormation templates.

## Pausing the environment

Scale all ECS tasks to 0 and stop the RDS instance. Cost while paused: ~$4/month (Route 53 + RDS storage).
