using Microsoft.Extensions.Logging;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Services;
using MeshChat.MAUI.ViewModels;
using MeshChat.MAUI.Views;

namespace MeshChat.MAUI;

/// <summary>
/// Configures the MAUI application with dependency injection, services, and UI components.
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// Builds and returns the configured MAUI application host.
    /// </summary>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Database path
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "meshchat.db");

        // Register Core services
        builder.Services.AddSingleton<ICryptoService>(sp =>
        {
            var crypto = new CryptoService();
            // On first launch, generate identity; on subsequent launches, load from secure storage
            // TODO: persist keys in iOS Keychain / Android Keystore
            crypto.GenerateIdentity();
            return crypto;
        });

        builder.Services.AddSingleton<IMessageStore>(sp => new MessageStore(dbPath));
        builder.Services.AddSingleton<IPeerStore>(sp => new PeerStore(dbPath));

        builder.Services.AddSingleton<MeshRouter>(sp =>
        {
            var crypto = sp.GetRequiredService<ICryptoService>();
            var messageStore = sp.GetRequiredService<IMessageStore>();
            var peerStore = sp.GetRequiredService<IPeerStore>();
            // Generate identity hash from the crypto service's public key
            var (publicKey, _) = crypto.GenerateIdentity();
            var myHash = crypto.GetPublicKeyHash(publicKey);
            return new MeshRouter(crypto, messageStore, peerStore, myHash);
        });

        // Register platform-specific BLE service
        builder.Services.AddSingleton<IBleService>(sp =>
        {
            var router = sp.GetRequiredService<MeshRouter>();
#if IOS
            return new MeshChat.BLE.iOS.BleStateManager(router);
#elif ANDROID
            return new MeshChat.BLE.Android.BleStateManager(
                Android.App.Application.Context, router);
#else
            throw new PlatformNotSupportedException("BLE is not supported on this platform.");
#endif
        });

        // Register network transports
        builder.Services.AddSingleton<TransportOrchestrator>(sp =>
        {
            var ble = sp.GetRequiredService<IBleService>();
            var crypto = sp.GetRequiredService<ICryptoService>();
            var (pubKey, _) = crypto.GenerateIdentity();
            var myHash = crypto.GetPublicKeyHash(pubKey);

            var localTransport = new MeshChat.Network.LocalNetworkTransport(9876, myHash);
            var relayTransport = new MeshChat.Network.RelayTransport("https://relay.meshchat.example.com", myHash);

            return new TransportOrchestrator(ble, localTransport, relayTransport);
        });

        // Register ViewModels
        builder.Services.AddTransient<ChatViewModel>();
        builder.Services.AddTransient<PeersViewModel>();

        // Register Pages
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<PeersPage>();

        return builder.Build();
    }
}
