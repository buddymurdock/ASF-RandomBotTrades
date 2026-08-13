# ASF-RandomBotTrades

Плагин для **[ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm)**, который через случайные интервалы дарит несколько карточек от одного вашего бота другому — чтобы в истории аккаунтов была живая торговая активность, а не полная тишина.

Трейд — **односторонний подарок** (ничего не запрашивается взамен) и отправляется только между ботами, которые **уже состоят в друзьях друг у друга** (например, через [ASF-RandomBotFriends](https://github.com/buddymurdock/ASF-RandomBotFriends)) — трейды между друзьями не требуют trade token и реже попадают под escrow-холд Steam. Передаются только **обычные и золотые карточки** (`TradingCard`/`FoilTradingCard`) из инвентаря Steam Community (appID 753) — никакой другой внутриигровой валюты или предметов.

Через случайную паузу в диапазоне `[MinDelayBetweenTrades; MaxDelayBetweenTrades]` секунд плагин выбирает случайного бота с запасом карточек и случайного его друга среди остальных ботов этого же ASF, собирает случайные `[MinItemsPerTrade; MaxItemsPerTrade]` карточек из его инвентаря и отправляет их подарком. За один тик отправляется не более одного трейда на весь инстанс ASF; пауза до следующего тика розыгрывается заново каждый раз, а не идёт с фиксированным периодом, чтобы не давать Steam ровный, легко фингерпринтящийся ритм запросов.

Если у отправляющего трейд-офера бота требуется подтверждение через мобильный аутентификатор — плагин подтверждает его сам (`Bot.Actions.HandleTwoFactorAuthenticationConfirmations`), тем же способом, что и штатная торговля ASF.

## Установка

1. Скачайте архив плагина из [Releases](../../releases) и распакуйте в папку `plugins` рядом с ASF (создайте подпапку с именем плагина).
2. Перезапустите ASF.

## Конфигурация

Настройки задаются **глобально**, в `ASF.json`, как дополнительные (нераспознанные ASF) свойства верхнего уровня:

```json
{
	"RandomBotTradesEnabled": true,
	"RandomBotTradesMinDelayBetweenTrades": 1800,
	"RandomBotTradesMaxDelayBetweenTrades": 7200,
	"RandomBotTradesMinItemsPerTrade": 1,
	"RandomBotTradesMaxItemsPerTrade": 3
}
```

| Свойство | Тип | По умолчанию | Описание |
| --- | --- | --- | --- |
| `RandomBotTradesEnabled` | `bool` | `false` | Включает/выключает плагин. |
| `RandomBotTradesMinDelayBetweenTrades` | `ushort`, секунды | `1800` | Нижняя граница случайной паузы между трейдами (один трейд за тик на весь инстанс ASF). |
| `RandomBotTradesMaxDelayBetweenTrades` | `ushort`, секунды | `7200` | Верхняя граница случайной паузы между трейдами. |
| `RandomBotTradesMinItemsPerTrade` | `byte` (1-255) | `1` | Нижняя граница случайного числа карточек за один подарок. |
| `RandomBotTradesMaxItemsPerTrade` | `byte` (1-255) | `3` | Верхняя граница случайного числа карточек за один подарок. |

Если у бота нет друзей среди других ботов этого ASF, или нет свободных tradable-карточек, плагин просто пропускает тик и пробует снова на следующем. Если `Min` больше `Max` в любой из пар, значения меняются местами автоматически.

## Сборка

Проект использует **[ASF-PluginTemplate](https://github.com/JustArchiNET/ASF-PluginTemplate)** и собирается вместе с исходниками ASF, подключёнными как git submodule:

```sh
git clone --recurse-submodules https://github.com/buddymurdock/ASF-RandomBotTrades.git
cd ASF-RandomBotTrades
dotnet build -c Release
```

Если репозиторий уже склонирован без `--recurse-submodules`, подтяните submodule отдельно:

```sh
git submodule update --init --recursive
```

## Лицензия

Apache-2.0, см. [LICENSE.txt](LICENSE.txt).
