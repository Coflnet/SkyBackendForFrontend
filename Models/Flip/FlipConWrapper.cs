using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Coflnet.Sky;
using Coflnet.Sky.Commands;

namespace hypixel
{
    public class FlipConWrapper
    {
        public IFlipConnection Connection;

        private Channel<LowPricedAuction> LowPriced = Channel.CreateBounded<LowPricedAuction>(100);

        private CancellationTokenSource cancellationTokenSource = null;

        public FlipConWrapper(IFlipConnection connection)
        {
            Connection = connection;
        }

        public async Task Work()
        {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();
            var stoppingToken = cancellationTokenSource.Token;
            var count = Connection.LatestSettings.Tier switch
            {
                AccountTier.PREMIUM => 3,
                AccountTier.SUPER_PREMIUM => 6,
                _ => 1
            };
            var limiter = new SemaphoreSlim(count);

            for (int i = 0; i < count; i++)
            {
                var worker = Task.Run(async () =>
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            var flip = await LowPriced.Reader.ReadAsync(stoppingToken);
                            //await limiter.WaitAsync();
                            await Connection.SendFlip(flip);
                        }
                        catch (Exception e)
                        {
                            dev.Logger.Instance.Error(e, "seding flip to " + Connection.UserId);
                        }
                        finally
                        {
                            //limiter.Release();
                        }
                    }
                });
            }


        }

        public bool AddLowPriced(LowPricedAuction lp)
        {
            var copy = new LowPricedAuction()
            {
                AdditionalProps = new System.Collections.Generic.Dictionary<string, string>(lp.AdditionalProps),
                Auction = lp.Auction,
                DailyVolume = lp.DailyVolume,
                Finder = lp.Finder,
                TargetPrice = lp.TargetPrice
            };
            if(Connection.Settings.FastMode)
                return Connection.SendFlip(copy).Result;
            
            return LowPriced.Writer.TryWrite(copy);
        }

        public Task<bool> SendFlip(FlipInstance flip)
        {
            return Connection.SendFlip(flip);
        }

        public void Stop()
        {
            cancellationTokenSource?.Cancel();
        }


    }
}