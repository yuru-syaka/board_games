using Amazon.ApiGatewayManagementApi;
using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using BoardGames.Server.Aws;
using BoardGames.Server.Games;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace BoardGames.Server;

/// <summary>
/// Lambda の入口。API Gateway（WebSocket）の 3 つのルートをすべてここで受ける。
///   $connect     接続した（何もしない。対局に入るまで記録もしない）
///   $disconnect  切断した（対局中なら相手に知らせて部屋を閉じる）
///   $default     メッセージが届いた
/// 環境変数: TABLE_NAME（DynamoDB のテーブル）、CALLBACK_URL（管理 API の URL）。CDK（infra/lib/game-stack.ts）が設定する。
/// </summary>
public sealed class Function
{
    // Lambda の実行環境が使い回される間は、クライアントとサーバーも使い回す（起動を速くする）
    static readonly Lazy<GameServer> Server = new(() =>
    {
        var table = Environment.GetEnvironmentVariable("TABLE_NAME") ?? throw new InvalidOperationException("TABLE_NAME is not set");
        var callback = Environment.GetEnvironmentVariable("CALLBACK_URL") ?? throw new InvalidOperationException("CALLBACK_URL is not set");
        var store = new DynamoMatchStore(new AmazonDynamoDBClient(), table);
        var push = new ApiGatewayPush(new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig { ServiceURL = callback }));
        return new GameServer(store, push, GameRegistry.Default());
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
