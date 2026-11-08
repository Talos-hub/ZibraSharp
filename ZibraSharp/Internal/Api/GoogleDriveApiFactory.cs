using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Internal.Ports;

namespace ZibraSharp.Internal.Api
{
    /// <summary>
    /// Creates a ready-to-use <see cref="GoogleDriveApi"/>:
    /// loads credentials.json, runs the OAuth flow (browser) when needed,
    /// stores the token in token.json, and builds the Drive service.
    /// </summary>
    public static class GoogleDriveApiFactory
    {
        private const string ApplicationName = "ZibraSharp";
        private const string UserId = "user";

        public static async Task<IApiCloud> CreateAsync(
            string? credentialsPath = null,
            string? tokenPath = null,
            CancellationToken ct = default)
        {
            var credentialsJson = await GoogleCredentialsLocator
                .LoadAsync(credentialsPath, ct)
                .ConfigureAwait(false);

            GoogleClientSecrets clientSecrets;
            try
            {
                using var stream = new MemoryStream(credentialsJson);
                clientSecrets = GoogleClientSecrets.FromStream(stream);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new AppException(
                    AppErrorCode.Parse,
                    nameof(CreateAsync),
                    "Failed to parse credentials.json as OAuth client secrets.",
                    ex);
            }

            UserCredential credential;
            try
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    clientSecrets.Secrets,
                    new[] { DriveService.Scope.DriveFile },
                    UserId,
                    ct,
                    new FileTokenStore(tokenPath ?? "token.json"))
                    .ConfigureAwait(false);

            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AppException(
                    AppErrorCode.Api,
                    nameof(CreateAsync),
                    "Google Drive authorization failed.",
                    ex);
            }

            var service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName
            });

            return new GoogleDriveApi(service);
        }
    }
}
