output "fulfillment_queue_url" { value = aws_sqs_queue.fulfillment.url }
output "fulfillment_queue_arn" { value = aws_sqs_queue.fulfillment.arn }
output "fulfillment_dlq_url" { value = aws_sqs_queue.fulfillment_dlq.url }
output "fulfillment_dlq_arn" { value = aws_sqs_queue.fulfillment_dlq.arn }
