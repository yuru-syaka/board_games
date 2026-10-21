using System.Text;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;

namespace BoardGames.Server.Aws;

/// <summary>API Gateway（WebSocket）の管理 API で、接続にメッセージを送る。</summary>
public sealed class ApiGatewayPush : IConnectionPush
{
    readonly IAmazonApiGatewayManagementApi _api;

    public ApiGatewayPush(IAmazonApiGatewayManagementApi api) => _api = api;

    public async Task<bool> SendAsync(string connectionId, string message)
    {
        try
        {
            await _api.PostToConnectionAsync(new PostToConnectionRequest
            {
                ConnectionId = connectionId,
                Data = new MemoryStream(Encoding.UTF8.GetBytes(message)),
            });
            return true;
        }
        catch (GoneException)
        {
            return false; // もう切れている接続（切断の通知は $disconnect で別に届く）
        }
    }
}
