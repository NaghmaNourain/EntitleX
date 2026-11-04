#!/usr/bin/env bash
# Creates LocalStack SQS queues used by EntitleX Phase 5 messaging.
set -euo pipefail

echo "Initializing EntitleX SQS queues..."

awslocal sqs create-queue --queue-name entitlex-fulfillment-dlq >/dev/null

DLQ_ARN=$(awslocal sqs get-queue-attributes \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/entitlex-fulfillment-dlq \
  --attribute-names QueueArn \
  --query 'Attributes.QueueArn' \
  --output text)

awslocal sqs create-queue \
  --queue-name entitlex-fulfillment \
  --attributes "{
    \"VisibilityTimeout\": \"30\",
    \"MessageRetentionPeriod\": \"1209600\",
    \"RedrivePolicy\": \"{\\\"deadLetterTargetArn\\\":\\\"${DLQ_ARN}\\\",\\\"maxReceiveCount\\\":\\\"5\\\"}\"
  }" >/dev/null

echo "SQS queues ready: entitlex-fulfillment (+ DLQ)"
