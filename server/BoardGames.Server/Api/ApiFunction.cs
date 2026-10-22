using Amazon.CognitoIdentityProvider;
using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using BoardGames.Server.Auth;
using BoardGames.Server.Aws;
using BoardGames.Server.Games;

namespace BoardGames.Server.Api;

/// <summary>
/// ランキング・プロフィール・マイページの API の Lambda の入口（ステップ 5-4）。API Gateway の HTTP API（ペイロード 2.0）から呼ばれる。
/// CloudFront の https://boardgamepia.com/api/* がここに届く。中身は PlayerApi（AWS に依存しないのでテストできる）。
/// 環境変数: PLAYERS_TABLE_NAME、RESULTS_TABLE_NAME、USER_POOL_ID、USER_POOL_CLIENT_ID（AWS_REGION は Lambda が設定する）。
/// CDK（infra/lib/game-stack.ts）が設定する。対戦サーバー（Function）と同じコードの別の入口。
/// </summary>
public sealed class ApiFunction
{
    static readonly Lazy<PlayerApi> Api = new(() =>
    {
        string Env(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name + " is not set");
        var region = Env("AWS_REGION");
        var userPoolId = Env("USER_POOL_ID");
        var store = new DynamoPlayerStore(new AmazonDynamoDBClient(), Env("PLAYERS_TABLE_NAME"), Env("RESULTS_TABLE_NAME"));
        var verifier = CognitoTokenVerifier.ForUserPool(region, userPoolId, Env("USER_POOL_CLIENT_ID"),
            new HttpClient { Timeout = TimeSpan.FromSeconds(3) });
        var accounts = new CognitoAccountDirectory(new AmazonCognitoIdentityProviderClient(), userPoolId);
        var games = GameRegistry.Default().Ids;
        return new PlayerApi(store, verifier, accounts, games, ApiSettings.Default, ServerSettings.Default.InitialRating);
    });

    public async Task<APIGatewayHttpApiV2ProxyResponse> HandleAsync(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        string? authorization = null;
        request.Headers?.TryGetValue("authorization", out authorization);
        var body = request.Body;
        if (body != null && request.IsBase64Encoded) body = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(body));

        ApiResponse response;
        try
        {
            response = await Api.Value.HandleAsync(new ApiRequest(
                request.RequestContext?.Http?.Method ?? "GET", request.RawPath ?? "/", authorization, body));
        }
        catch (Exception e)
        {
            context.Logger.LogError($"api error {request.RequestContext?.Http?.Method} {request.RawPath}: {e.GetType().Name}: {e.Message}");
            response = new ApiResponse(500, "{\"error\":\"server_error\",\"message\":\"サーバーでエラーが起きました。時間をおいてお試しください。\"}", "no-store");
        }

        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = response.Status,
            Body = response.Body,
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json; charset=utf-8",
                ["Cache-Control"] = response.CacheControl,
            },
        };
    }
}
