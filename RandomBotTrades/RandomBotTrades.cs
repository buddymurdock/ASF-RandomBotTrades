using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using JetBrains.Annotations;
using SteamKit2;

namespace RandomBotTrades;

#pragma warning disable CA1812 // ASF uses this class during runtime
#pragma warning disable CA1001 // Plugin instances live for the process' lifetime; ASF gives IPlugin implementations no disposal hook to call into
#pragma warning disable CA5394 // Randomness here only picks an arbitrary bot/item/order, it's not used for anything security-sensitive
[UsedImplicitly]
internal sealed class RandomBotTrades : IASF, IGitHubPluginUpdates {
	private static readonly HashSet<EAssetType> AllowedItemTypes = [EAssetType.TradingCard, EAssetType.FoilTradingCard];

	private const byte DefaultMaxItemsPerTrade = 3;
	private const ushort DefaultMaxDelayBetweenTradesInSeconds = 7200;
	private const byte DefaultMinItemsPerTrade = 1;
	private const ushort DefaultMinDelayBetweenTradesInSeconds = 1800;

	private CancellationTokenSource? BackgroundLoopCts;
	private bool Enabled;
	private ushort MaxDelayBetweenTradesInSeconds = DefaultMaxDelayBetweenTradesInSeconds;
	private byte MaxItemsPerTrade = DefaultMaxItemsPerTrade;
	private ushort MinDelayBetweenTradesInSeconds = DefaultMinDelayBetweenTradesInSeconds;
	private byte MinItemsPerTrade = DefaultMinItemsPerTrade;

	public string Name => nameof(RandomBotTrades);
	public string RepositoryName => "buddymurdock/ASF-RandomBotTrades";
	public Version Version => typeof(RandomBotTrades).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	// Reads RandomBotTradesEnabled / RandomBotTradesMinDelayBetweenTrades / RandomBotTradesMaxDelayBetweenTrades /
	// RandomBotTradesMinItemsPerTrade / RandomBotTradesMaxItemsPerTrade from the global ASF.json config
	public Task OnASFInit(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
		if (additionalConfigProperties != null) {
			foreach ((string configProperty, JsonElement configValue) in additionalConfigProperties) {
				switch (configProperty) {
					case $"{nameof(RandomBotTrades)}Enabled" when configValue.ValueKind is JsonValueKind.True or JsonValueKind.False:
						Enabled = configValue.GetBoolean();

						break;
					case $"{nameof(RandomBotTrades)}MinDelayBetweenTrades" when (configValue.ValueKind == JsonValueKind.Number) && configValue.TryGetUInt16(out ushort minDelayBetweenTrades) && (minDelayBetweenTrades > 0):
						MinDelayBetweenTradesInSeconds = minDelayBetweenTrades;

						break;
					case $"{nameof(RandomBotTrades)}MaxDelayBetweenTrades" when (configValue.ValueKind == JsonValueKind.Number) && configValue.TryGetUInt16(out ushort maxDelayBetweenTrades) && (maxDelayBetweenTrades > 0):
						MaxDelayBetweenTradesInSeconds = maxDelayBetweenTrades;

						break;
					case $"{nameof(RandomBotTrades)}MinItemsPerTrade" when (configValue.ValueKind == JsonValueKind.Number) && configValue.TryGetByte(out byte minItemsPerTrade) && (minItemsPerTrade > 0):
						MinItemsPerTrade = minItemsPerTrade;

						break;
					case $"{nameof(RandomBotTrades)}MaxItemsPerTrade" when (configValue.ValueKind == JsonValueKind.Number) && configValue.TryGetByte(out byte maxItemsPerTrade) && (maxItemsPerTrade > 0):
						MaxItemsPerTrade = maxItemsPerTrade;

						break;
				}
			}
		}

		if (MinDelayBetweenTradesInSeconds > MaxDelayBetweenTradesInSeconds) {
			(MinDelayBetweenTradesInSeconds, MaxDelayBetweenTradesInSeconds) = (MaxDelayBetweenTradesInSeconds, MinDelayBetweenTradesInSeconds);
		}

		if (MinItemsPerTrade > MaxItemsPerTrade) {
			(MinItemsPerTrade, MaxItemsPerTrade) = (MaxItemsPerTrade, MinItemsPerTrade);
		}

		if (!Enabled) {
			ASF.ArchiLogger.LogGenericInfo($"{Name} is disabled, set {nameof(RandomBotTrades)}Enabled to true in ASF.json to turn it on.");

			return Task.CompletedTask;
		}

		ASF.ArchiLogger.LogGenericInfo($"{Name} is enabled, {MinDelayBetweenTradesInSeconds}-{MaxDelayBetweenTradesInSeconds}s between trades, {MinItemsPerTrade}-{MaxItemsPerTrade} tradable trading cards per gift, between bots that are already friends with each other.");

		if (BackgroundLoopCts != null) {
			// OnASFInit() should only ever be called once per process, this is just a safety net against a possible double start
			return Task.CompletedTask;
		}

		BackgroundLoopCts = new CancellationTokenSource();

		Utilities.InBackground(() => BackgroundLoopAsync(BackgroundLoopCts.Token), true);

		return Task.CompletedTask;
	}

	public Task OnLoaded() {
		ASF.ArchiLogger.LogGenericInfo($"{Name} has been loaded!");

		return Task.CompletedTask;
	}

	// Delay is re-rolled every tick within [MinDelayBetweenTradesInSeconds; MaxDelayBetweenTradesInSeconds] instead of a fixed-period timer -
	// a perfectly metronomic tick interval running around the clock is itself a machine-detectable pattern, independent of anything visible to other users
	private async Task BackgroundLoopAsync(CancellationToken cancellationToken) {
		while (!cancellationToken.IsCancellationRequested) {
			int delaySeconds = MinDelayBetweenTradesInSeconds == MaxDelayBetweenTradesInSeconds ? MinDelayBetweenTradesInSeconds : Random.Shared.Next(MinDelayBetweenTradesInSeconds, MaxDelayBetweenTradesInSeconds + 1);

			try {
				await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
			} catch (OperationCanceledException) {
				break;
			}

			try {
				await TrySendSingleTradeAsync().ConfigureAwait(false);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}
		}
	}

	// Sends at most one one-way trade (gift, nothing requested back) per call, from a random bot with spare tradable cards
	// towards a random other bot it's already Steam-friends with (no trade token needed, less likely to hit an escrow hold)
	private async Task TrySendSingleTradeAsync() {
		IReadOnlyDictionary<string, Bot>? bots = Bot.BotsReadOnly;

		if ((bots == null) || (bots.Count < 2)) {
			return;
		}

		List<Bot> onlineBots = [.. bots.Values.Where(static bot => bot.IsConnectedAndLoggedOn).OrderBy(static _ => Random.Shared.Next())];

		foreach (Bot sender in onlineBots) {
			List<Bot> friendBots = [.. onlineBots.Where(otherBot => (otherBot != sender) && (otherBot.SteamID != 0) && (sender.SteamFriends.GetFriendRelationship(otherBot.SteamID) == EFriendRelationship.Friend))];

			if (friendBots.Count == 0) {
				continue;
			}

			List<Asset> tradableCards = await GetTradableCardsAsync(sender).ConfigureAwait(false);

			if (tradableCards.Count < MinItemsPerTrade) {
				continue;
			}

			Bot receiver = friendBots[Random.Shared.Next(friendBots.Count)];

			int itemCount = Math.Min(MinItemsPerTrade == MaxItemsPerTrade ? MinItemsPerTrade : Random.Shared.Next(MinItemsPerTrade, MaxItemsPerTrade + 1), tradableCards.Count);

			List<Asset> itemsToGive = [.. tradableCards.OrderBy(static _ => Random.Shared.Next()).Take(itemCount)];

			(bool success, _, HashSet<ulong>? mobileTradeOfferIDs) = await sender.ArchiWebHandler.SendTradeOffer(receiver.SteamID, itemsToGive).ConfigureAwait(false);

			if (!success) {
				sender.ArchiLogger.LogGenericWarning($"Failed to send a gift of {itemsToGive.Count} card(s) to {receiver.BotName}.");

				return;
			}

			sender.ArchiLogger.LogGenericInfo($"Sent a gift of {itemsToGive.Count} card(s) to {receiver.BotName}.");

			if (mobileTradeOfferIDs?.Count > 0) {
				(bool twoFactorSuccess, _, string message) = await sender.Actions.HandleTwoFactorAuthenticationConfirmations(true, EMobileConfirmationType.Trade, mobileTradeOfferIDs, true).ConfigureAwait(false);

				if (!twoFactorSuccess) {
					sender.ArchiLogger.LogGenericWarning($"Failed to confirm the trade via the mobile authenticator: {message}");
				}
			}

			return;
		}
	}

	private static async Task<List<Asset>> GetTradableCardsAsync(Bot bot) {
		List<Asset> tradableCards = [];

		await foreach (Asset asset in bot.ArchiWebHandler.GetInventoryAsync(appID: Asset.SteamAppID, contextID: Asset.SteamCommunityContextID)) {
			if (asset.Tradable && AllowedItemTypes.Contains(asset.Type)) {
				tradableCards.Add(asset);
			}
		}

		return tradableCards;
	}
}
#pragma warning restore CA5394 // Randomness here only picks an arbitrary bot/item/order, it's not used for anything security-sensitive
#pragma warning restore CA1001 // Plugin instances live for the process' lifetime; ASF gives IPlugin implementations no disposal hook to call into
#pragma warning restore CA1812 // ASF uses this class during runtime
