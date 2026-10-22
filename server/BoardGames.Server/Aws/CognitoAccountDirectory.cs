using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using BoardGames.Server.Api;

namespace BoardGames.Server.Aws;

/// <summary>ログインのアカウント（Cognito のユーザープール）。アカウントの削除で、ユーザーを消す（ステップ 5-4）。</summary>
public sealed class CognitoAccountDirectory : IAccountDirectory
{
    readonly IAmazonCognitoIdentityProvider _cognito;
    readonly string _userPoolId;

    public CognitoAccountDirectory(IAmazonCognitoIdentityProvider cognito, string userPoolId)
    {
        _cognito = cognito;
        _userPoolId = userPoolId;
    }

    public async Task DeleteUserAsync(string username)
    {
        try
        {
            await _cognito.AdminDeleteUserAsync(new AdminDeleteUserRequest { UserPoolId = _userPoolId, Username = username });
        }
        catch (UserNotFoundException)
        {
            // もう消えている（削除をやり直したとき）
        }
    }
}
