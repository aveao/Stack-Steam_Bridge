using ChatExchangeDotNet;
using SteamKit2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SSB
{
    class Program
    {
        static SteamClient steamClient;
        static CallbackManager manager;

        static SteamUser steamUser;

        static SteamFriends steamFriends;
        static bool isRunning;

        static string user, pass;
        static string authCode, twoFactorAuth;
        static Room sandbox;
        static List<string> configfile;
        static int lastmessageno = 0;
        static void Main(string[] args)
        {
            if (File.Exists("config.ini"))
            {
                configfile = File.ReadAllLines("config.ini").ToList();
                if (configfile.Count() >= 6)
                {
                    var client = new Client(configfile[0], configfile[1]);
                    sandbox = client.JoinRoom(configfile[5]);

                    sandbox.EventManager.ConnectListener(EventType.InternalException, new Action<Exception>(ex => Console.WriteLine("[ERROR] " + ex)));

                    sandbox.EventManager.ConnectListener(EventType.MessagePosted, new Action<Message>(message =>
                    {
                        lastmessageno = message.ID;
                        steamFriends.SendChatMessage(new SteamID(configfile[4]), EChatEntryType.ChatMsg, string.Format("({2}) {0}: {1}", message.Author.Name, message.Content, message.ID));
                    }));

                    user = configfile[2];
                    pass = configfile[3];
                    steamClient = new SteamClient();
                    manager = new CallbackManager(steamClient);
                    steamUser = steamClient.GetHandler<SteamUser>();
                    steamFriends = steamClient.GetHandler<SteamFriends>();
                    manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
                    manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

                    manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
                    manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
                    manager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);

                    manager.Subscribe<SteamFriends.FriendMsgCallback>(OnMessage);
                    isRunning = true;

                    Console.WriteLine("Connecting to Steam...");

                    SteamDirectory.Initialize().Wait();
                    steamClient.Connect();

                    while (isRunning)
                    {
                        manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                    }
                }
            }
        }

        static void OnMessage(SteamFriends.FriendMsgCallback callback)
        {
            if (callback.EntryType == EChatEntryType.ChatMsg && callback.Sender == new SteamID(configfile[4]))
            {
                if (callback.Message.StartsWith("!!"))
                {
                    var command = callback.Message.Substring(0, callback.Message.IndexOf(" "));
                    switch (command)
                    {
                        case "!!starlast":
                            var starstatuslast = sandbox.ToggleStar(lastmessageno);
                            steamFriends.SendChatMessage(new SteamID(configfile[4]), EChatEntryType.ChatMsg, ((starstatuslast) ? "Starred successfully" : "Starring failed"));
                            break;
                        case "!!remove":
                            var deletestatus = sandbox.DeleteMessage(int.Parse(callback.Message.Replace("!!remove", "").Trim()));
                            steamFriends.SendChatMessage(new SteamID(configfile[4]), EChatEntryType.ChatMsg, ((deletestatus) ? "Deleted successfully" : "Deleting failed"));
                            break;
                        case "!!star":
                            var starstatus = sandbox.ToggleStar(int.Parse(callback.Message.Replace("!!star", "").Trim()));
                            steamFriends.SendChatMessage(new SteamID(configfile[4]), EChatEntryType.ChatMsg, ((starstatus) ? "Starred successfully" : "Starring failed"));
                            break;
                        case "!!pin":
                            var pinstatus = sandbox.TogglePin(int.Parse(callback.Message.Replace("!!star", "").Trim()));
                            steamFriends.SendChatMessage(new SteamID(configfile[4]), EChatEntryType.ChatMsg, ((pinstatus) ? "Pinned successfully" : "Pinning failed"));
                            break;
                        case "!!starcount":
                            var starcount = sandbox.GetMessage(int.Parse(callback.Message.Replace("!!starcount", "").Trim())).StarCount;
                            steamFriends.SendChatMessage(new SteamID(configfile[4]), EChatEntryType.ChatMsg, "Amount of stars: " + starcount);
                            break;
                    }
                }
                else
                {
                    sandbox.PostMessageLight(callback.Message);
                }
            }
        }

        static void OnConnected(SteamClient.ConnectedCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                Console.WriteLine("Unable to connect to Steam: {0}", callback.Result);

                isRunning = false;
                return;
            }

            Console.WriteLine("Connected to Steam! Logging in '{0}'...", user);

            byte[] sentryHash = null;
            if (File.Exists("sentry.bin"))
            {
                byte[] sentryFile = File.ReadAllBytes("sentry.bin");
                sentryHash = CryptoHelper.SHAHash(sentryFile);
            }

            steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = user,
                Password = pass,
                AuthCode = authCode,
                TwoFactorCode = twoFactorAuth,
                SentryFileHash = sentryHash,
            });
        }

        static void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            Console.WriteLine("Disconnected from Steam, reconnecting in 5...");

            Thread.Sleep(TimeSpan.FromSeconds(5));

            steamClient.Connect();
        }

        static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            bool isSteamGuard = callback.Result == EResult.AccountLogonDenied;
            bool is2FA = callback.Result == EResult.AccountLoginDeniedNeedTwoFactor;

            if (isSteamGuard || is2FA)
            {
                Console.WriteLine("This account is SteamGuard protected!");

                if (is2FA)
                {
                    Console.Write("Please enter your 2 factor auth code from your authenticator app: ");
                    twoFactorAuth = Console.ReadLine();
                }
                else
                {
                    Console.Write("Please enter the auth code sent to the email at {0}: ", callback.EmailDomain);
                    authCode = Console.ReadLine();
                }

                return;
            }

            if (callback.Result != EResult.OK)
            {
                Console.WriteLine("Unable to logon to Steam: {0} / {1}", callback.Result, callback.ExtendedResult);

                isRunning = false;
                return;
            }

            Console.WriteLine("Successfully logged on!");

            steamFriends.SetPersonaState(EPersonaState.Online);
        }

        static void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Console.WriteLine("Logged off of Steam: {0}", callback.Result);
        }

        static void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback)
        {
            Console.WriteLine("Updating sentryfile...");
            
            int fileSize;
            byte[] sentryHash;
            using (var fs = File.Open("sentry.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                fs.Seek(callback.Offset, SeekOrigin.Begin);
                fs.Write(callback.Data, 0, callback.BytesToWrite);
                fileSize = (int)fs.Length;

                fs.Seek(0, SeekOrigin.Begin);
                using (var sha = new SHA1CryptoServiceProvider())
                {
                    sentryHash = sha.ComputeHash(fs);
                }
            }
            
            steamUser.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
            {
                JobID = callback.JobID,

                FileName = callback.FileName,

                BytesWritten = callback.BytesToWrite,
                FileSize = fileSize,
                Offset = callback.Offset,

                Result = EResult.OK,
                LastError = 0,

                OneTimePassword = callback.OneTimePassword,

                SentryFileHash = sentryHash,
            });

            Console.WriteLine("Done!");
        }

    }
}
