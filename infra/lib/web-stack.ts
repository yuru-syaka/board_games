import * as fs from 'fs';
import * as path from 'path';
import * as cdk from 'aws-cdk-lib/core';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as s3deploy from 'aws-cdk-lib/aws-s3-deployment';
import * as cloudfront from 'aws-cdk-lib/aws-cloudfront';
import * as origins from 'aws-cdk-lib/aws-cloudfront-origins';
import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as route53targets from 'aws-cdk-lib/aws-route53-targets';
import { Construct } from 'constructs';

export interface WebStackProps extends cdk.StackProps {
  /** 独自ドメイン（例: boardgamepia.com）。www 付きはこのドメインへ転送する */
  domainName: string;
  /** CertStack（us-east-1）で発行した証明書 */
  certificate: acm.ICertificate;
}

/**
 * サイト配信: 非公開の S3 バケット + CloudFront（OAC で CloudFront からだけ読める）。
 * web/ の中身をデプロイ時にアップロードし、CloudFront のキャッシュも消す。
 * 独自ドメイン（apex と www）を CloudFront に向け、www は apex へ 301 転送する。
 * *.cloudfront.net の URL も引き続き使える。
 */
export class WebStack extends cdk.Stack {
  readonly distribution: cloudfront.Distribution;

  constructor(scope: Construct, id: string, props: WebStackProps) {
    super(scope, id, props);
    const domain = props.domainName;

    const bucket = new s3.Bucket(this, 'SiteBucket', {
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      encryption: s3.BucketEncryption.S3_MANAGED,
      enforceSSL: true,
      // 開発中は cdk destroy で中身ごと消せるようにしておく
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      autoDeleteObjects: true,
    });

    // www 付きのアクセスは apex へ転送し、
    // "/blog/" や "/blog" を "/blog/index.html" に読み替える（S3 は自動でディレクトリの index を返さないため）
    const indexRewrite = new cloudfront.Function(this, 'IndexRewrite', {
      runtime: cloudfront.FunctionRuntime.JS_2_0,
      code: cloudfront.FunctionCode.fromInline(`
function handler(event) {
  var request = event.request;
  var uri = request.uri;
  var host = request.headers.host ? request.headers.host.value : '';
  if (host === 'www.${domain}') {
    var qs = Object.keys(request.querystring).map(function (k) {
      var v = request.querystring[k].value;
      return v ? k + '=' + v : k;
    }).join('&');
    return {
      statusCode: 301,
      statusDescription: 'Moved Permanently',
      headers: { location: { value: 'https://${domain}' + uri + (qs ? '?' + qs : '') } },
    };
  }
  if (uri.endsWith('/')) {
    request.uri = uri + 'index.html';
  } else if (!uri.split('/').pop().includes('.')) {
    request.uri = uri + '/index.html';
  }
  return request;
}`),
    });

    // ブラウザ側の防御機能を有効にするレスポンスヘッダー
    // Google AdSense が使うドメイン（広告の読み込み・表示・計測・同意メッセージ）
    const ads = [
      'https://pagead2.googlesyndication.com',
      'https://*.googlesyndication.com',
      'https://*.doubleclick.net',
      'https://*.googleadservices.com',
      'https://adservice.google.com',
      'https://*.google.com',
      'https://*.gstatic.com',
      'https://*.adtrafficquality.google',
      'https://fundingchoicesmessages.google.com',
    ].join(' ');
    const csp = [
      "default-src 'self'",
      // Unity WebGL の起動に wasm の実行とインラインの起動スクリプトが必要
      `script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval' blob: ${ads}`,
      "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
      "font-src 'self' https://fonts.gstatic.com",
      "img-src 'self' data: blob: https:", // 広告の画像は配信元が多いため https を許可
      "media-src 'self' blob:",
      // PUN2（Photon Cloud）への WebSocket。AWS 版のゲームサーバーに移ったら外す
      `connect-src 'self' blob: data: https://*.exitgames.com wss://*.exitgames.com https://*.photonengine.io wss://*.photonengine.io ${ads}`,
      `frame-src ${ads}`,
      "worker-src 'self' blob:",
      "frame-ancestors 'self'",
      "object-src 'none'",
      "base-uri 'self'",
    ].join('; ');

    const securityHeaders = new cloudfront.ResponseHeadersPolicy(this, 'SecurityHeaders', {
      comment: 'board pia security headers',
      securityHeadersBehavior: {
        strictTransportSecurity: { accessControlMaxAge: cdk.Duration.days(365), includeSubdomains: false, override: true },
        contentTypeOptions: { override: true },
        frameOptions: { frameOption: cloudfront.HeadersFrameOption.SAMEORIGIN, override: true },
        referrerPolicy: { referrerPolicy: cloudfront.HeadersReferrerPolicy.STRICT_ORIGIN_WHEN_CROSS_ORIGIN, override: true },
      },
      customHeadersBehavior: {
        customHeaders: [
          // まずは「報告のみ」。ブラウザのコンソールに違反が出ないことを確かめてから強制に切り替える
          { header: 'Content-Security-Policy-Report-Only', value: csp, override: true },
          { header: 'Permissions-Policy', value: 'camera=(), microphone=(), geolocation=(), payment=()', override: true },
        ],
      },
    });

    const distribution = new cloudfront.Distribution(this, 'SiteDistribution', {
      comment: 'board pia site',
      defaultRootObject: 'index.html',
      priceClass: cloudfront.PriceClass.PRICE_CLASS_200, // 日本を含み、高い地域を除く
      domainNames: [domain, `www.${domain}`],
      certificate: props.certificate,
      minimumProtocolVersion: cloudfront.SecurityPolicyProtocol.TLS_V1_2_2021, // 古い TLS 1.0 / 1.1 を使わせない
      defaultBehavior: {
        origin: origins.S3BucketOrigin.withOriginAccessControl(bucket),
        viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        cachePolicy: cloudfront.CachePolicy.CACHING_OPTIMIZED,
        responseHeadersPolicy: securityHeaders,
        compress: true,
        functionAssociations: [
          { function: indexRewrite, eventType: cloudfront.FunctionEventType.VIEWER_REQUEST },
        ],
      },
    });

    // サイト（HTML・CSS・画像など）。play/ は下のゲームごとのデプロイに任せる。
    // exclude したファイルは prune（S3 側の不要ファイル削除）の対象にもならないので、ゲームのビルドは消えない。
    const webDir = path.join(__dirname, '..', '..', 'web');
    new s3deploy.BucketDeployment(this, 'DeploySite', {
      sources: [s3deploy.Source.asset(webDir, { exclude: ['play/**'] })],
      destinationBucket: bucket,
      exclude: ['play/*'],
      distribution,
      distributionPaths: ['/*'],
    });

    // Unity の WebGL ビルド（git 管理外）。手元の web/play/<slug>/ にあるゲームだけをアップロードする。
    // ビルドがない PC からデプロイしても、S3 にあるゲームは消えない（そのゲームのデプロイ自体を作らないため。
    // BucketDeployment は削除時にファイルを残す）。prune はゲームのフォルダの中だけに効くので、古いビルドは消える。
    const playDir = path.join(webDir, 'play');
    const games = fs.existsSync(playDir)
      ? fs.readdirSync(playDir).filter((slug) => fs.existsSync(path.join(playDir, slug, 'index.html')))
      : [];
    for (const slug of games) {
      new s3deploy.BucketDeployment(this, `DeployPlay-${slug}`, {
        sources: [s3deploy.Source.asset(path.join(playDir, slug), { exclude: ['unity-build.provenance.json'] })],
        destinationBucket: bucket,
        destinationKeyPrefix: `play/${slug}/`,
        memoryLimit: 1024, // Unity の WebGL ビルド（数十 MB）を展開するため
        distribution,
        distributionPaths: [`/play/${slug}/*`],
      });
    }

    // Route 53: apex と www を CloudFront に向ける（IPv4 = A、IPv6 = AAAA）。CloudFront 向けのエイリアスは問い合わせ無料
    const zone = route53.HostedZone.fromLookup(this, 'Zone', { domainName: domain });
    const target = route53.RecordTarget.fromAlias(new route53targets.CloudFrontTarget(distribution));
    for (const [id, recordName] of [['Apex', domain], ['Www', `www.${domain}`]]) {
      new route53.ARecord(this, `${id}A`, { zone, recordName, target });
      new route53.AaaaRecord(this, `${id}Aaaa`, { zone, recordName, target });
    }

    this.distribution = distribution;
    new cdk.CfnOutput(this, 'SiteUrl', { value: `https://${domain}/` });
    new cdk.CfnOutput(this, 'CloudFrontUrl', { value: `https://${distribution.distributionDomainName}/` });
    new cdk.CfnOutput(this, 'BucketName', { value: bucket.bucketName });
  }
}
