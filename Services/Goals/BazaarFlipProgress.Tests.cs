using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared.Goals;

public class BazaarFlipProgressTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static Goal NewGoal() => new() { Id = "flip", CreatedAt = Now, RootItemTag = "COAL", Quantity = 64,
        Title = "Coal flip", BazaarFlip = new(), Root = new() { Id = "root", Kind = StepKind.Sell, ItemTag = "COAL", Quantity = 64 } };
    private static BazaarFlipOrder Order(bool sell = false, int filled = 0, int claimed = 0, bool active = true, int second = 1, int amount = 64, DateTime? closedAt = null)
        => new() { CreatedAt = Now.AddSeconds(second), IsSell = sell, Amount = amount, Filled = filled, Claimed = claimed, Active = active, ClosedAt = closedAt };
    private static GoalProgress Eval(Goal goal) => GoalProgressEvaluator.Evaluate(goal, new Dictionary<string,long>(), new Dictionary<string,long>(), new HashSet<string>(), Now);
    private static bool Chat(Goal goal, string message) => GoalChatProgress.Apply(
        new GoalList { Goals = { goal }, FocusId = goal.Id }, message, _ => "Coal", Now.AddSeconds(5));

    [Test]
    public void CompleteJourneyRequiresFinalCoinClaimAndSurvivesReconnect()
    {
        var g = NewGoal();
        var buy=Order(); var sell=Order(true,second:4);
        Assert.That(BazaarFlipProgress.Next(g), Is.EqualTo(BazaarFlipAction.Buy));
        BazaarFlipProgress.Observe(g,new[] { buy },1);
        Assert.That(BazaarFlipProgress.Next(g), Is.EqualTo(BazaarFlipAction.WaitBuy));
        buy.Filled=64; BazaarFlipProgress.Observe(g,new[] { buy },2);
        Assert.That(BazaarFlipProgress.Next(g), Is.EqualTo(BazaarFlipAction.ClaimItems));
        buy.Claimed=64; buy.Active=false; BazaarFlipProgress.Observe(g,new[] { buy },3);
        Assert.That(BazaarFlipProgress.Next(g), Is.EqualTo(BazaarFlipAction.Sell));
        BazaarFlipProgress.Observe(g,new[] { buy,sell },4);
        Assert.That(BazaarFlipProgress.Next(g), Is.EqualTo(BazaarFlipAction.WaitSell));
        Assert.That(Eval(g).Complete, Is.False);
        sell.Filled=64; BazaarFlipProgress.Observe(g,new[] { buy,sell },5);
        g=JsonConvert.DeserializeObject<Goal>(JsonConvert.SerializeObject(g));
        Assert.That(BazaarFlipProgress.Next(g), Is.EqualTo(BazaarFlipAction.ClaimCoins));
        Assert.That(Eval(g).Complete, Is.False);
        sell.Claimed=64; sell.Active=false; BazaarFlipProgress.Observe(g,new[] { buy,sell },6);
        Assert.That(Eval(g).Complete, Is.True);
        Assert.That(Eval(g).Done, Is.EqualTo(6));
    }

    [Test]
    public void PartialClaimsCanBeSoldWhileTheRemainingBuyFills()
    {
        var g=NewGoal();var buy=Order(filled:32,claimed:16);
        BazaarFlipProgress.Observe(g,new[] { buy },1);
        Assert.That(g.BazaarFlip.BuyClaimed, Is.EqualTo(16));
        Assert.That(BazaarFlipProgress.Next(g), Is.EqualTo(BazaarFlipAction.Sell));
        Assert.That(BazaarFlipProgress.Line(g,true,"Coal","Coal").Text, Does.Contain("16x Coal"));
        var sell=Order(true,amount:16,second:2);
        BazaarFlipProgress.Observe(g,new[] { buy,sell },2);
        Assert.That(BazaarFlipProgress.Next(g), Is.EqualTo(BazaarFlipAction.ClaimItems));
        Assert.That(Eval(g).Complete,Is.False);
    }

    [Test]
    public void CancelledPartialBuyRetainsClaimsAndOrdersOnlyRemainingQuantity()
    {
        var g=NewGoal();var buy=Order(filled:32,claimed:32,active:false);
        BazaarFlipProgress.Observe(g,new[] { buy },1);
        var sell=Order(true,filled:32,claimed:32,active:false,second:2,amount:32);
        BazaarFlipProgress.Observe(g,new[] { buy,sell },2);
        Assert.That(BazaarFlipProgress.Next(g),Is.EqualTo(BazaarFlipAction.Buy));
        Assert.That(BazaarFlipProgress.Line(g,true,"Coal","Coal").OnClick,Is.EqualTo("/cofl bzorder 32 Coal"));
        BazaarFlipProgress.Observe(g,new[] { buy,sell,Order(second:3,amount:32) },3);
        Assert.That(BazaarFlipProgress.Committed(g.BazaarFlip,false),Is.EqualTo(64));
    }

    [Test]
    public void CancelledSellOffersReturnUnsoldQuantityToSellGuidance()
    {
        var g=NewGoal();var buy=Order(filled:64,claimed:64,active:false,closedAt:Now.AddSeconds(1));
        var sell=Order(true,filled:16,claimed:16,active:false,second:2);
        BazaarFlipProgress.Observe(g,new[] { buy,sell },1);
        Assert.That(BazaarFlipProgress.Next(g),Is.EqualTo(BazaarFlipAction.Sell));
        Assert.That(BazaarFlipProgress.Line(g,true,"Coal","Coal").Text,Does.Contain("48x Coal"));
        Assert.That(Eval(g).Complete,Is.False);
    }

    [Test]
    public void RepeatedStaleOrRegressingSnapshotsCannotDoubleCountOrUndoEvidence()
    {
        var g=NewGoal();var buy=Order(filled:32,claimed:16);
        BazaarFlipProgress.Observe(g,new[] { buy },2);
        Assert.That(BazaarFlipProgress.Observe(g,new[] { buy },3),Is.False);
        Assert.That(BazaarFlipProgress.Observe(g,new[] { Order(filled:64,claimed:64) },1),Is.False);
        BazaarFlipProgress.Observe(g,new[] { Order() },4);
        Assert.That(g.BazaarFlip.BuyClaimed,Is.EqualTo(16));
        Assert.That(BazaarFlipProgress.Filled(g.BazaarFlip,false),Is.EqualTo(32));
    }

    [Test]
    public void ChatReplayAndMissingOrdersNeverProveClaims()
    {
        var g=NewGoal();BazaarFlipProgress.Observe(g,new[] { Order(filled:64) },1);
        var message="[Bazaar] Claimed 64x Coal worth 134.4 coins bought for 2.1 each!";
        Assert.That(Chat(g,message),Is.False);Assert.That(Chat(g,message),Is.False);
        BazaarFlipProgress.Observe(g,Array.Empty<BazaarFlipOrder>(),2);
        Assert.That(g.BazaarFlip.BuyClaimed,Is.Zero);
        Assert.That(BazaarFlipProgress.Next(g),Is.EqualTo(BazaarFlipAction.ClaimItems));
    }

    [Test]
    public void UnrelatedSalesAndExistingHoldingsCannotCompleteFlip()
    {
        var g=NewGoal();var old=Order(filled:64,claimed:64);old.CreatedAt=Now.AddSeconds(-1);
        BazaarFlipProgress.Observe(g,new[] { old,Order(true,filled:64,claimed:64,second:2) },1);
        Assert.That(g.BazaarFlip.Orders,Is.Empty);
        Assert.That(Chat(g,"[Bazaar] Sell Offer Setup! 64x Coal for 134.4 coins."),Is.False);
        Assert.That(GoalProgressEvaluator.Evaluate(g,new Dictionary<string,long> { ["COAL"]=64 },new Dictionary<string,long>(),new HashSet<string>(),Now).Complete,Is.False);
    }

    [Test]
    public void ReconnectedFinalTombstonesRecoverCompleteJourneyWithoutChat()
    {
        var g=JsonConvert.DeserializeObject<Goal>(JsonConvert.SerializeObject(NewGoal()));
        BazaarFlipProgress.Observe(g,new[] { Order(filled:64,claimed:64,active:false,closedAt:Now.AddSeconds(1)),Order(true,filled:64,claimed:64,active:false,second:2) },5);
        Assert.That(Eval(g).Complete,Is.True);
        Assert.That(BazaarFlipProgress.Observe(g,new[] { Order() },4),Is.False);
    }

    [Test]
    public void OrderQuantityAllocationCannotExceedGoalOrAcquiredItems()
    {
        var g=NewGoal();var buy=Order(filled:32,claimed:32,amount:128);
        BazaarFlipProgress.Observe(g,new[] { buy },1);
        BazaarFlipProgress.Observe(g,new[] { buy,Order(true,filled:128,claimed:128,second:2,amount:128) },2);
        Assert.That(g.BazaarFlip.BuyClaimed,Is.EqualTo(32));
        Assert.That(g.BazaarFlip.SellClaimed,Is.EqualTo(32));
        Assert.That(Eval(g).Complete,Is.False);
    }

    [Test]
    public void OneSnapshotClosesMissingOrderBeforeAllocatingItsReplacement()
    {
        var g=NewGoal();
        BazaarFlipProgress.Observe(g,new[] { Order() },1);
        BazaarFlipProgress.Observe(g,new[] { Order(second:2) },2);
        Assert.That(g.BazaarFlip.Orders.Count(o=>o.Active),Is.EqualTo(1));
        Assert.That(g.BazaarFlip.Orders.Single(o=>o.Active).CreatedAt,Is.EqualTo(Now.AddSeconds(2)));
        Assert.That(BazaarFlipProgress.Next(g),Is.EqualTo(BazaarFlipAction.WaitBuy));
    }

    [Test]
    public void EarlierSellOfferCannotBeAdoptedAfterLaterBuysAppear()
    {
        var g=NewGoal();var sell=Order(true,filled:64,claimed:64,second:1);
        BazaarFlipProgress.Observe(g,new[] { sell },1);
        BazaarFlipProgress.Observe(g,new[] { sell,Order(filled:64,claimed:64,second:2) },2);
        Assert.That(g.BazaarFlip.SellClaimed,Is.Zero);
        Assert.That(Eval(g).Complete,Is.False);
        Assert.That(BazaarFlipProgress.Next(g),Is.EqualTo(BazaarFlipAction.Sell));
    }

    [Test]
    public void RegressingOrderAmountCannotDropBelowRetainedFillAndClaimEvidence()
    {
        var g=NewGoal();BazaarFlipProgress.Observe(g,new[] { Order(filled:64,claimed:64) },1);
        BazaarFlipProgress.Observe(g,new[] { Order(amount:1) },2);
        var order=g.BazaarFlip.Orders.Single();
        Assert.That(order.Amount,Is.GreaterThanOrEqualTo(order.Filled));
        Assert.That(order.Filled,Is.GreaterThanOrEqualTo(order.Claimed));
        Assert.That(g.BazaarFlip.BuyClaimed,Is.EqualTo(64));
    }

    [Test]
    public void EarlierSellAllocationCannotGrowAfterLaterBuyClaims()
    {
        var g=NewGoal();var buy=Order(filled:16,claimed:16);var sell=Order(true,second:2);
        BazaarFlipProgress.Observe(g,new[] { buy },1);
        BazaarFlipProgress.Observe(g,new[] { buy,sell },2);
        Assert.That(g.BazaarFlip.Orders.Single(o=>o.IsSell).Amount,Is.EqualTo(16));
        g=JsonConvert.DeserializeObject<Goal>(JsonConvert.SerializeObject(g));
        buy.Filled=64;buy.Claimed=64;sell.Filled=64;sell.Claimed=64;
        BazaarFlipProgress.Observe(g,new[] { buy,sell },3);
        Assert.That(g.BazaarFlip.Orders.Single(o=>o.IsSell).Amount,Is.EqualTo(16));
        Assert.That(g.BazaarFlip.SellClaimed,Is.EqualTo(16));
        Assert.That(Eval(g).Complete,Is.False);
        Assert.That(BazaarFlipProgress.Next(g),Is.EqualTo(BazaarFlipAction.Sell));
        Assert.That(BazaarFlipProgress.Line(g,true,"Coal","Coal").Text,Does.Contain("48x Coal"));
        BazaarFlipProgress.Observe(g,new[] { buy,sell,Order(true,filled:48,claimed:48,active:false,second:3,amount:48) },4);
        Assert.That(Eval(g).Complete,Is.True);
    }

    [Test]
    public void ReplayedSaleBeforeBuyClosureCannotUseLaterClaims()
    {
        var g=NewGoal();
        BazaarFlipProgress.Observe(g,new[] { Order(filled:64,claimed:64,active:false,closedAt:Now.AddSeconds(4)),
            Order(true,filled:64,claimed:64,active:false,second:2) },1);
        Assert.That(g.BazaarFlip.SellClaimed,Is.Zero);
        Assert.That(Eval(g).Complete,Is.False);
        g=JsonConvert.DeserializeObject<Goal>(JsonConvert.SerializeObject(g));
        BazaarFlipProgress.Observe(g,new[] { Order(filled:64,claimed:64,active:false,closedAt:Now.AddSeconds(4)),
            Order(true,filled:64,claimed:64,active:false,second:2) },2);
        Assert.That(g.BazaarFlip.SellClaimed,Is.Zero,"reconnect must not retrospectively adopt the rejected sale");
    }

    [Test]
    public void ExistingSaleStillTracksClaimsAfterLaterBuyClosure()
    {
        var g=NewGoal();var buy=Order(filled:16,claimed:16);
        BazaarFlipProgress.Observe(g,new[] { buy },1);
        var sell=Order(true,filled:16,amount:16,second:2);
        BazaarFlipProgress.Observe(g,new[] { buy,sell },2);
        buy.Filled=64;buy.Claimed=64;buy.Active=false;buy.ClosedAt=Now.AddSeconds(4);
        BazaarFlipProgress.Observe(g,new[] { buy,sell },3);
        sell.Claimed=16;sell.Active=false;
        BazaarFlipProgress.Observe(g,new[] { buy,sell },4);
        Assert.That(g.BazaarFlip.SellClaimed,Is.EqualTo(16));
        Assert.That(g.BazaarFlip.Orders.Single(o=>o.IsSell).Amount,Is.EqualTo(16));
        Assert.That(g.BazaarFlip.IgnoredSellOrders,Does.Not.Contain(sell.CreatedAt));
        Assert.That(BazaarFlipProgress.Line(g,true,"Coal","Coal").Text,Does.Contain("48x Coal"));
    }

    [Test]
    public void PreviouslyObservedFinalClaimCannotFinanceNewlyArrivingHistoricalSale()
    {
        var g=NewGoal();var buy=Order(filled:64,claimed:64,active:false,closedAt:Now.AddSeconds(4));
        BazaarFlipProgress.Observe(g,new[] { buy },1);
        g=JsonConvert.DeserializeObject<Goal>(JsonConvert.SerializeObject(g));
        BazaarFlipProgress.Observe(g,new[] { buy,Order(true,filled:64,claimed:64,active:false,second:2) },2);
        Assert.That(g.BazaarFlip.SellClaimed,Is.Zero);
        Assert.That(Eval(g).Complete,Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NewlyObservedPartialClaimsCannotFinanceEarlierSale(bool cancelled)
    {
        var g=NewGoal();
        BazaarFlipProgress.Observe(g,new[] { Order(filled:32,claimed:32,active:!cancelled,
                closedAt:cancelled ? Now.AddSeconds(1) : null),
            Order(true,filled:32,claimed:32,second:2,amount:32) },1);
        Assert.That(g.BazaarFlip.SellClaimed,Is.Zero);
    }

    [Test]
    public void PersistedPartialClaimsCapSaleWhileNewClaimsArrive()
    {
        var g=NewGoal();var buy=Order(filled:16,claimed:16);
        BazaarFlipProgress.Observe(g,new[] { buy },1);
        g=JsonConvert.DeserializeObject<Goal>(JsonConvert.SerializeObject(g));
        buy.Filled=64;buy.Claimed=64;
        BazaarFlipProgress.Observe(g,new[] { buy,Order(true,filled:64,claimed:64,second:2) },2);
        Assert.That(g.BazaarFlip.SellClaimed,Is.EqualTo(16));
        Assert.That(Eval(g).Complete,Is.False);
    }

    [Test]
    public void LateClaimEvidenceAdvancesClosureBoundWithoutIncreasingEarlierSaleAllocation()
    {
        var g=NewGoal();var buy=Order(filled:64,claimed:16,active:false,closedAt:Now.AddSeconds(1));
        BazaarFlipProgress.Observe(g,new[] { buy },1);
        buy.Claimed=64;buy.ClosedAt=Now.AddSeconds(4);
        BazaarFlipProgress.Observe(g,new[] { buy,Order(true,filled:64,claimed:64,second:2) },2);
        Assert.That(g.BazaarFlip.Orders.Single(o=>!o.IsSell).ClosedAt,Is.EqualTo(Now.AddSeconds(4)));
        Assert.That(g.BazaarFlip.SellClaimed,Is.EqualTo(16));
        buy.ClosedAt=Now.AddSeconds(1);
        BazaarFlipProgress.Observe(g,new[] { buy },3);
        Assert.That(g.BazaarFlip.Orders.Single(o=>!o.IsSell).ClosedAt,Is.EqualTo(Now.AddSeconds(4)));
    }

    [Test]
    public void ClosedAtAndClaimsSurviveSerializationForLaterSellEvidence()
    {
        var g=NewGoal();var buy=Order(filled:64,claimed:64,active:false,closedAt:Now.AddSeconds(1));
        BazaarFlipProgress.Observe(g,new[] { buy },1);
        g=JsonConvert.DeserializeObject<Goal>(JsonConvert.SerializeObject(g));
        Assert.That(g.BazaarFlip.Orders.Single().ClosedAt,Is.EqualTo(buy.ClosedAt));
        BazaarFlipProgress.Observe(g,new[] { buy,Order(true,filled:64,claimed:64,active:false,second:2) },2);
        Assert.That(Eval(g).Complete,Is.True);
    }

    [Test]
    public void NoCookieGuidanceUsesNpcSearchAndManualOrderManagement()
    {
        var g=NewGoal();
        Assert.That(BazaarFlipProgress.Line(g,false,"Coal","Coal").OnClick,Is.EqualTo("/cofl bzorder search 64 Coal"));
        BazaarFlipProgress.Observe(g,new[] { Order(filled:64) },1);
        Assert.That(BazaarFlipProgress.Line(g,false,"Coal","Coal").OnClick,Is.Null);
        Assert.That(BazaarFlipProgress.Line(g,false,"Coal","Coal").Hover,Does.Contain("Bazaar NPC"));
    }

    [TestCase(0.9, "simulation", ">90% chance", true)]
    [TestCase(0.95, "prospective", ">95% chance", false)]
    [TestCase(null, null, ">95% chance", false)]
    public void QualifiedHoverUsesThresholdAndEvidenceSource(double? threshold, string source, string expected, bool simulated)
    {
        var g = NewGoal();
        g.Plan = new BazaarPlanInfo { Mode = "Investing", ConfidenceStatus = "Qualified", LowerBound = 0.93, QualifyThreshold = threshold, EvidenceSource = source };
        var hover = BazaarFlipProgress.Line(g, false, "Coal", "Coal").Hover;
        Assert.That(hover, Does.Contain(expected).And.Contain("lower bound 93%"));
        const string note = "simulated from market history, not yet confirmed by tracked trades";
        if (simulated) Assert.That(hover, Does.Contain(note)); else Assert.That(hover, Does.Not.Contain(note));
    }

    private static Goal WolfToothPlan(string mode = "Active")
    {
        var g = NewGoal();
        g.RootItemTag = g.Root.ItemTag = "WOLF_TOOTH";
        g.Quantity = g.Root.Quantity = 4383;
        // quoted from a market snapshot whose best buy order was 376.5
        g.Plan = new BazaarPlanInfo { Mode = mode, GeneratedAt = Now, OriginalQuantity = 4383, EntryPrice = 376.6, ExitPrice = 390, SellTaxRate = 0.0125, EstimatedNetProfit = 50_000 };
        return g;
    }

    private static Dictionary<string, BazaarTopOrder> TopOrder(double price, DateTime at) => new() { ["WOLF_TOOTH"] = new(price, at) };

    /// <summary>
    /// Regression: the buy step kept the entry price of the quote (376.6) while the order book had moved on (best buy order 376.9),
    /// so the suggested custom price queued the order behind three better ones. The Active rule (best + one tick) is re-applied.
    /// </summary>
    [Test]
    public void ActivePlanBuyStepUsesBestBuyOrderObservedAfterTheQuote()
    {
        var g = WolfToothPlan();

        var step = GoalProgressEvaluator.Evaluate(g, new Dictionary<string, long>(), new Dictionary<string, long>(), new HashSet<string>(), Now,
            TopOrder(376.9, Now.AddMinutes(20))).Actionable.Single();

        Assert.That(step.Kind, Is.EqualTo(StepKind.BuyOrder));
        Assert.That(step.UnitPrice, Is.EqualTo(377.0));
    }

    [Test]
    public void WithoutFreshObservationBuyStepKeepsPlannedEntry()
    {
        var g = WolfToothPlan();

        Assert.That(Eval(g).Actionable.Single().UnitPrice, Is.EqualTo(376.6));
        Assert.That(BazaarFlipProgress.EntryPrice(g, new BazaarTopOrder(376.9, Now.AddMinutes(-1))), Is.EqualTo(376.6), "older than the quote");
        Assert.That(BazaarFlipProgress.EntryPrice(g, new BazaarTopOrder(0, Now.AddMinutes(1))), Is.EqualTo(376.6), "no buy orders");
        Assert.That(BazaarFlipProgress.EntryPrice(NewGoal(), new BazaarTopOrder(376.9, Now.AddMinutes(1))), Is.Null, "no plan");
    }

    /// <summary>Investing entries are a policy quantile below the best buy order, outbidding it would change the plan.</summary>
    [Test]
    public void InvestingPlanKeepsPlannedEntry()
    {
        var g = WolfToothPlan("Investing");

        Assert.That(BazaarFlipProgress.EntryPrice(g, new BazaarTopOrder(376.9, Now.AddMinutes(20))), Is.EqualTo(376.6));
    }

    /// <summary>Regression: the hud said "@ 377" (short format) while the step said 376.6; both now show the same exact price.</summary>
    [Test]
    public void HudLineShowsTheSamePriceAsTheBuyStep()
    {
        var g = WolfToothPlan();
        var list = new GoalList { Goals = { g }, FocusId = g.Id };
        string Line(Dictionary<string, BazaarTopOrder> top)
        {
            var progress = GoalProgressEvaluator.Evaluate(g, new Dictionary<string, long>(), new Dictionary<string, long>(), new HashSet<string>(), Now, top);
            return GoalHud.BuildDisplay(list, new Dictionary<string, GoalProgress> { [g.Id] = progress }, true, _ => "Wolf Tooth", _ => "Wolf Tooth", null)
                .StepLines()[0].Text;
        }

        Assert.That(Line(null), Is.EqualTo("§aPlace buy order: 4,383x Wolf Tooth §7@ §6376.6"));
        Assert.That(Line(TopOrder(376.9, Now.AddMinutes(20))), Is.EqualTo("§aPlace buy order: 4,383x Wolf Tooth §7@ §6377"));
    }

    [TestCase(376.6, "376.6")]
    [TestCase(377.0, "377")]
    [TestCase(2_999_999.7, "2,999,999.7")]
    public void OrderPriceIsExact(double price, string expected)
        => Assert.That(GoalHud.OrderPrice(price), Is.EqualTo(expected));

    private static string HudLine(Goal g, Dictionary<string, BazaarTopOrder> top, out string hover)
    {
        var progress = GoalProgressEvaluator.Evaluate(g, new Dictionary<string, long>(), new Dictionary<string, long>(), new HashSet<string>(), Now, top);
        var line = GoalHud.BuildDisplay(new GoalList { Goals = { g }, FocusId = g.Id }, new Dictionary<string, GoalProgress> { [g.Id] = progress },
            true, _ => "Wolf Tooth", _ => "Wolf Tooth", null).StepLines()[0];
        hover = line.Hover;
        return line.Text;
    }

    /// <summary>A refreshed entry at or above the exit after tax (390 * 0.9875 = 385.1) is kept but flagged instead of silently suggested.</summary>
    [Test]
    public void RefreshedEntryWithoutMarginIsFlagged()
    {
        var g = WolfToothPlan();

        var text = HudLine(g, TopOrder(386, Now.AddMinutes(20)), out var hover);

        Assert.That(text, Is.EqualTo(BazaarFlipProgress.NoMarginWarning + "§aPlace buy order: 4,383x Wolf Tooth §7@ §6386.1"));
        // (390 * 0.9875 - 386.1) * 4383 = -4,273.4
        Assert.That(hover, Does.Contain("net est. -4.27k after 1.25% tax"));
        Assert.That(BazaarFlipProgress.NoMargin(g.Plan, 385.1), Is.False);
        Assert.That(BazaarFlipProgress.NoMargin(g.Plan, 385.2), Is.True);
        Assert.That(BazaarFlipProgress.NoMargin(new BazaarPlanInfo { ExitPrice = 0 }, 385.2), Is.False, "no exit price, no claim");
    }

    /// <summary>The hover recomputes the net estimate for a refreshed entry and keeps the engine's figure for the planned one.</summary>
    [Test]
    public void NetEstimateFollowsTheEffectiveEntry()
    {
        var g = WolfToothPlan();

        HudLine(g, null, out var planned);
        HudLine(g, TopOrder(376.9, Now.AddMinutes(20)), out var refreshed);

        Assert.That(planned, Does.Contain("net est. +50k after"));
        // (390 * 0.9875 - 377) * 4383 = 35,611.9
        Assert.That(refreshed, Does.Contain("net est. +35.6k after"));
    }

    /// <summary>
    /// The player's own partial buy order on top (one order fitting its unfilled rest) is not outbid: the entry beats the best
    /// other order instead, which keeps the same queue position for less.
    /// </summary>
    [Test]
    public void OwnTopOrderIsNotOutbid()
    {
        var g = WolfToothPlan();
        BazaarFlipProgress.Observe(g, new[] { Order(amount: 2000, filled: 500, second: 30) }, 1);
        var at = Now.AddMinutes(20);

        Assert.That(BazaarFlipProgress.EntryPrice(g, new BazaarTopOrder(377.0, at, 1500, 1, 376.9)), Is.EqualTo(377.0), "second best + tick");
        Assert.That(BazaarFlipProgress.EntryPrice(g, new BazaarTopOrder(377.4, at, 1500, 1, 376.9)), Is.EqualTo(377.0), "cheaper than matching the own order");
        Assert.That(BazaarFlipProgress.EntryPrice(g, new BazaarTopOrder(377.0, at, 1500, 1)), Is.EqualTo(377.0), "alone on the book: match the own price");
        Assert.That(BazaarFlipProgress.EntryPrice(g, new BazaarTopOrder(377.0, at, 1501, 1, 376.9)), Is.EqualTo(377.1), "more than the own rest: someone else");
        Assert.That(BazaarFlipProgress.EntryPrice(g, new BazaarTopOrder(377.0, at, 1500, 2, 376.9)), Is.EqualTo(377.1), "two orders on top");
        Assert.That(BazaarFlipProgress.EntryPrice(WolfToothPlan(), new BazaarTopOrder(377.0, at, 1500, 1, 376.9)), Is.EqualTo(377.1), "no own order");
    }

    [TestCase(376.65, false, "376.7")]
    [TestCase(1234.5, false, "1234.5")]
    [TestCase(1234.04, true, "1,234")]
    public void OrderPriceRoundsToTheTick(double price, bool grouping, string expected)
        => Assert.That(GoalHud.OrderPrice(price, grouping), Is.EqualTo(expected));
}
