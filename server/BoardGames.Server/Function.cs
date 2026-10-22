using Amazon.ApiGatewayManagementApi;
using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using BoardGames.Server.Auth;
using BoardGames.Server.Aws;
using BoardGames.Server.Games;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace BoardGames.Server;

/// <summary>
/// Lambda の入口。API Gateway（WebSocket）の 3 つのルートをすべてここで受ける。
///   $connect     接続した（何もしない。対局に入るまで記録もしない）
///   $disconnect  切断した（対局中なら席を「離席中」にして相手に知らせる。それ以外は部屋から抜ける）
///   $default     メッセージが届いた
/// 環境変数: TABLE_NAME（対局中のデータ）、RESULTS_TABLE_NAME（対局結果）、PLAYERS_TABLE_NAME（戦績）、
/// CALLBACK_URL（管理 API の URL）、USER_POOL_ID・USER_POOL_CLIENT_ID（ログインの確認。AWS_REGION は Lambda が設定する）。
/// CDK（infra/lib/game-stack.ts）が設定する。
/// </summary>
public sealed class Function
{
    // Lambda の実行環境が使い回される間は、クライアントとサーバーも使い回す（起動を速くする）
    static readonly Lazy<GameServer> Server = new(() =>
    {
        var table = Environment.GetEnvironmentVariable("TABLE_NAME") ?? throw new InvalidOperationException("TABLE_NAME is not set");
        var results = Environment.GetEnvironmentVariable("RESULTS_TABLE_NAME") ?? throw new InvalidOperationException("RESULTS_TABLE_NAME is not set");
        var callback = Environment.GetEnvironmentVariable("CALLBACK_URL") ?? throw new InvalidOperationException("CALLBACK_URL is not set");
        var players = Environment.GetEnvironmentVariable("PLAYERS_TABLE_NAME") ?? throw new InvalidOperationException("PLAYERS_TABLE_NAME is not set");
        var store = new DynamoMatchStore(new AmazonDynamoDBClient(), table, results, players);
        var push = new ApiGatewayPush(new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig { ServiceURL = callback }));

        // ログイン（Cognito）。設定がなければログインは受け付けない（全員ログインしていない扱いで遊べる）
        var region = Environment.GetEnvironmentVariable("AWS_REGION");
        var userPoolId = Environment.GetEnvironmentVariable("USER_POOL_ID");
        var clientId = Environment.GetEnvironmentVariable("USER_POOL_CLIENT_ID");
        var verifier = string.IsNullOrEmpty(region) || string.IsNullOrEmpty(userPoolId) || string.IsNullOrEmpty(clientId)
            ? null
            : CognitoTokenVerifier.ForUserPool(region, userPoolId, clientId, new HttpClient { Timeout = TimeSpan.FromSeconds(3) });
        return new GameServer(store, push, GameRegistry.Default(), verifier: verifier);
    });

    public async Task<APIGatewayProxyResponse> HandleAsync(APIGatewayProxyRequest request, ILambdaContext context)
    {
        var connectionId = request.RequestContext.ConnectionId;
        switch (request.RequestContext.RouteKey)
        {
            case "$connect":
                break;
            case "$disconnect":
                await Server.Value.OnDisconnectAsync(connectionId);
                break;
            default:
                await Server.Value.OnMessageAsync(connectionId, request.Body);
                break;
        }
        return new APIGatewayProxyResponse { StatusCode = 200 };
    }
}
