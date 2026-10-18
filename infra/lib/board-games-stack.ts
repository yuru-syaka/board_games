import * as cdk from 'aws-cdk-lib/core';
import { Construct } from 'constructs';

// Phase 0: intentionally empty. Phase 1 splits this into WebStack (S3 + CloudFront) and others.
export class BoardGamesStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);
  }
}
