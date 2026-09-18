variable "name" {
  description = "Resource name prefix."
  type        = string
}

variable "region" {
  description = "AWS region, used with each subnet map's key to build its availability zone (e.g. region \"us-east-1\" + key \"a\" -> \"us-east-1a\")."
  type        = string
}

variable "vpc_cidr" {
  description = "CIDR block for the VPC."
  type        = string
}

variable "public_subnets" {
  description = "Public subnets (ALB + ECS tasks), keyed by availability zone suffix, e.g. { a = \"10.0.1.0/24\" }."
  type        = map(string)
}

variable "private_subnets" {
  description = "Private subnets (RDS + ElastiCache), keyed by availability zone suffix."
  type        = map(string)
}
