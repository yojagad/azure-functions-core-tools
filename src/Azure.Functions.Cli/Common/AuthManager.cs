using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.HostActions;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Colors.Net.StringStaticMethods;
namespace Azure.Functions.Cli.Common
{
    internal class AuthManager : IAuthManager
    {
        private readonly ISecretsManager _secretsManager;

        public AuthManager(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
            var connectionString = secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals("AzureWebJobsStorage", StringComparison.OrdinalIgnoreCase)).Value;
        }
        public async Task CreateAADApplication(string accessToken, string appName)
        {
            if (string.IsNullOrEmpty(appName))
            {
                throw new CliArgumentsException("Must specify name of new Azure Active Directory application with --app-name parameter.",
                    new CliArgument { Name = "app-name", Description = "Name of new Azure Active Directory application" });
            }
            if (CommandChecker.CommandExists("az"))
            {
                // OAuth is port sensitive. There is no way of using a wildcard in the reply URLs to allow for variable ports
                // Set the port in the reply URLs to the default used by the Functions Host
                int port = StartHostAction.DefaultPort;

                // Assemble all of the necessary parameters
                string identifierUrl = "https://" + appName + ".localhost.net";
                string homepage = "http://localhost:" + port;
                string authCallback = "/.auth/login/aad/callback";
                string localhostSSL = "https://localhost:" + port + authCallback;
                string localhost = "http://localhost:" + port + authCallback;

                var replyUrls = new List<string>
                {
                    localhostSSL,
                    localhost
                };

                replyUrls.Sort();
                string serializedReplyUrls = string.Join(" ", replyUrls.ToArray<string>());
                string clientSecret = GeneratePassword(128);

                // Assemble the required resources in the proper format
                var resourceList = new List<requiredResourceAccess>();
                var access = new requiredResourceAccess();
                access.resourceAppId = AADConstants.ServicePrincipals.AzureADGraph;
                access.resourceAccess = new resourceAccess[]
                {
                    new resourceAccess {  type = AADConstants.ResourceAccessTypes.User, id = AADConstants.Permissions.EnableSSO.ToString() }
                };

                resourceList.Add(access);

                // It is easiest to pass them in the right format to the az CLI via a (temp) file + filename
                string requiredResourcesFilename = $"{clientSecret}.txt";
                File.WriteAllText(requiredResourcesFilename, JsonConvert.SerializeObject(resourceList));

                string query = $"--display-name {appName} --homepage {homepage} --identifier-uris {identifierUrl} --password {clientSecret}" +
                    $" --reply-urls {serializedReplyUrls} --oauth2-allow-implicit-flow true --required-resource-access @{requiredResourcesFilename}";

                var az = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                   ? new Executable("cmd", $"/c az ad app create {query}")
                   : new Executable("az", $"ad app create {query}");
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                int exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
                var response = stdout.ToString().Trim(' ', '\n', '\r', '"');

                // Clean up file we created to pass data in proper format to az CLI
                File.Delete($"{requiredResourcesFilename}");

                if (exitCode != 0)
                {
                    ColoredConsole.WriteLine(Red(stderr.ToString().Trim(' ', '\n', '\r', '"')));
                    return;
                }
                // Update function application's (local) auth settings
                JObject application = JObject.Parse(response);
                var jwt = new JwtSecurityToken(accessToken);
                string tenantId = jwt.Payload["tid"] as string;
                CreateAuthSettings(homepage, (string)application["appId"], clientSecret, tenantId, replyUrls);
                ColoredConsole.WriteLine(Green(response));

                ColoredConsole.WriteLine(Yellow($"This application will only work for the Function Host default port of {port}"));
            }
            else
            {
                throw new FileNotFoundException("Cannot find az cli. `auth create-aad` requires the Azure CLI.");
            }
        }

        public void CreateAuthSettings(string homepage, string clientId, string clientSecret, string tenant, List<string> replyUrls)
        {
            // The WEBSITE_AUTH_ALLOWED_AUDIENCES setting is of the form "{replyURL1} {replyURL2}"
            string serializedReplyUrls = string.Join(" ", replyUrls.ToArray<string>());

            // Create a local auth .json file that will be used by the middleware
            var middlewareAuthSettingsFile = SecretsManager.MiddlewareAuthSettingsFileName;
            var middlewareAuthSettings = new AuthSettingsFile(middlewareAuthSettingsFile);
            middlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_AUTO_AAD", "True");
            middlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_CLIENT_ID", clientId);
            middlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_CLIENT_SECRET", clientSecret);
            middlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_DEFAULT_PROVIDER", "AzureActiveDirectory");
            middlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_ENABLED", "True");
            middlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_OPENID_ISSUER", "https://sts.windows.net/" + tenant + "/");
            middlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_RUNTIME_VERSION", "1.0.0");
            middlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_TOKEN_STORE", "true");
            middlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_UNAUTHENTICATED_ACTION", "AllowAnonymous");

            // Middleware requires signing and encryption keys for local testing
            // These will be different than the encryption and signing keys used by the application in production
            string encryptionKey = ComputeSha256Hash(clientSecret);
            string signingKey = ComputeSha256Hash(clientId);
            middlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_ENCRYPTION_KEY", encryptionKey);
            middlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_SIGNING_KEY", signingKey);
            middlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_ALLOWED_AUDIENCES", serializedReplyUrls);
            middlewareAuthSettings.Commit();
        }
    
        /// <summary>
        /// The format of the auth settings understood by the middleware is different than that of the
        /// auth settings understood by published Azure Websites/Functions
        /// In order to keep the two in-sync, auto-generate the published auth settings from the middleware auth settings
        /// TODO - change this based on feedback about customer experience 
        /// </summary>
        public static void PublishAuthSettings()
        {
            // Read the middleware auth settings from the local file
            var middlewareAuthSettingsFile = SecretsManager.MiddlewareAuthSettingsFileName;
            var middlewareAuthSettings = new AuthSettingsFile(middlewareAuthSettingsFile);
            var existingSettings = middlewareAuthSettings.GetValues();
            
            // Create a local auth .json file to update the Site's auth settings via /config/authsettings
            var authSettingsFile = SecretsManager.AuthSettingsFileName;
            var authsettings = new AuthSettingsFile(authSettingsFile);

            // Some of the values match 1:1, just with different keys
            var keyMap = new Dictionary<string, string>
            {
                { "WEBSITE_AUTH_AUTO_AAD", "isAadAutoProvisioned" },
                { "WEBSITE_AUTH_CLIENT_ID", "clientId" },
                { "WEBSITE_AUTH_CLIENT_SECRET", "clientSecret" },
                { "WEBSITE_AUTH_ENABLED", "enabled" },
                { "WEBSITE_AUTH_OPENID_ISSUER", "issuer" },
                { "WEBSITE_AUTH_RUNTIME_VERSION", "runtimeVersion"},
                { "WEBSITE_AUTH_TOKEN_STORE", "tokenStoreEnabled" }
            };

            foreach (var keyPair in keyMap)
            {
                // Map from existing settings' keys to the published keys
                authsettings.SetAuthSetting(keyPair.Value, existingSettings[keyPair.Key]);
            }

            // Re-format the reply URLs
            // 'allowedAudiences' setting of /config/authsettings is of the form ["{replyURL1}", "{replyURL2}"]
            string serializedReplyUrls = existingSettings["WEBSITE_AUTH_ALLOWED_AUDIENCES"];
            var replyUrls = serializedReplyUrls.Split(' ');
            string serializedArray = JsonConvert.SerializeObject(replyUrls, Formatting.Indented);
            authsettings.SetAuthSetting("allowedAudiences", serializedArray);

            // Map Default Provider to an integer ("AzureActiveDirectory" maps to "0")
            string provider = existingSettings["WEBSITE_AUTH_DEFAULT_PROVIDER"];
            ProvidersEnum enumProvider;
            Enum.TryParse(provider, out enumProvider);
            authsettings.SetAuthSetting("defaultProvider", ((int) enumProvider).ToString());

            // Map Unauthenticated Client Action to an integer ("AllowAnonymous" maps to "1")
            string unauthClientAction = existingSettings["WEBSITE_AUTH_UNAUTHENTICATED_ACTION"];
            UnauthenticatedClientAction enumAction;
            Enum.TryParse(unauthClientAction, out enumAction);
            authsettings.SetAuthSetting("unauthenticatedClientAction", ((int)enumAction).ToString()); 
            authsettings.Commit();
        }

        public static bool CleanupPublishAuthSettings()
        {
            var authSettingsFile = SecretsManager.AuthSettingsFileName;
            if (File.Exists(authSettingsFile))
            {
                File.Delete(authSettingsFile);
                return true;
            }

            return false;
        }

        // **So far, only tested with AzureActiveDirectory
        public enum ProvidersEnum { AzureActiveDirectory, Facebook, Twitter, MicrosoftAccount, Google };

        public enum UnauthenticatedClientAction { RedirectToLoginPage, AllowAnonymous };

        public async Task DeleteAADApplication(string id)
        {
            if (CommandChecker.CommandExists("az"))
            {
                string query = $"--id {id}";

                var az = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? new Executable("cmd", $"/c az ad app delete {query}")
                    : new Executable("az", $"ad app delete {query}");
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();

                var exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
                if (exitCode != 0)
                {
                    throw new CliException(stderr.ToString().Trim(' ', '\n', '\r'));
                }
                else
                {
                    // Successful delete call does not return anything, so write success message
                    ColoredConsole.WriteLine(Green($"AAD Application {id} successfully deleted"));
                }
            }
            else
            {
                throw new FileNotFoundException("Cannot find az cli. `auth delete-aad` requires the Azure CLI.");
            }
        }

        static string ComputeSha256Hash(string rawData)
        {
            // Create a SHA256   
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // ComputeHash - returns byte array  
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                // Convert byte array to a string   
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public static string GeneratePassword(int length)
        {
            const string PasswordChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHJKLMNPQRSTWXYZ0123456789#$";
            string pwd = GetRandomString(PasswordChars, length);

            while (!MeetsConstraint(pwd))
            {
                pwd = GetRandomString(PasswordChars, length);
            }

            return pwd;
        }

        private static string GetRandomString(string allowedChars, int length)
        {
            StringBuilder retVal = new StringBuilder(length);
            byte[] randomBytes = new byte[length * 4];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(randomBytes);

                for (int i = 0; i < length; i++)
                {
                    int seed = BitConverter.ToInt32(randomBytes, i * 4);
                    Random random = new Random(seed);
                    retVal.Append(allowedChars[random.Next(allowedChars.Length)]);
                }
            }

            return retVal.ToString();
        }

        private static bool MeetsConstraint(string password)
        {
            return !string.IsNullOrEmpty(password) &&
                password.Any(c => char.IsUpper(c)) &&
                password.Any(c => char.IsLower(c)) &&
                password.Any(c => char.IsDigit(c)) &&
                password.Any(c => !char.IsLetterOrDigit(c));
        }
    }

    static class AADConstants
    {
        public static class ServicePrincipals
        {
            public const string AzureADGraph = "00000002-0000-0000-c000-000000000000";
        }

        public static class Permissions
        {
            public static readonly Guid AccessApplication = new Guid("92042086-4970-4f83-be1c-e9c8e2fab4c8");
            public static readonly Guid EnableSSO = new Guid("311a71cc-e848-46a1-bdf8-97ff7156d8e6");
            public static readonly Guid ReadDirectoryData = new Guid("5778995a-e1bf-45b8-affa-663a9f3f4d04");
            public static readonly Guid ReadAndWriteDirectoryData = new Guid("78c8a3c8-a07e-4b9e-af1b-b5ccab50a175");
        }

        public static class ResourceAccessTypes
        {
            public const string Application = "Role";
            public const string User = "Scope";
        }
    }

    class resourceAccess
    {
        public string id { get; set; }
        public string type { get; set; }
    }

    class requiredResourceAccess
    {
        public string resourceAppId { get; set; }
        public resourceAccess[] resourceAccess { get; set; }
    }
}