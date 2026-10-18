#!/usr/bin/env node
import * as cdk from 'aws-cdk-lib/core';
import { BoardGamesStack } from '../lib/board-games-stack';

const app = new cdk.App();

// Account comes from the CLI profile (--profile board-game-pia-admin); region is fixed to Tokyo.
new BoardGamesStack(app, 'BoardGamesStack', {
  env: { account: process.env.CDK_DEFAULT_ACCOUNT, region: 'ap-northeast-1' },
});
