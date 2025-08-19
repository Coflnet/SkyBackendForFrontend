using System;
using StackExchange.Redis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.Shared
{
    public class SelfUpdatingValue<T> : IDisposable
    {
        public T Value { get; private set; }
        public event Action<T> OnChange;
        public event Action<T> AfterChange;
        public Func<T, bool> ShouldPreventUpdate;
        private string UserId;
        private string Key;
        private bool IsDisposed = false;
        private DateTime LastUpdateReceived = DateTime.UtcNow;

        ChannelMessageQueue subTask;

        private SelfUpdatingValue(string userId, string key)
        {
            this.UserId = userId;
            this.Key = key;
        }

        public static async Task<SelfUpdatingValue<T>> Create(string userId, string key, Func<T> defaultGetter = null)
        {
            var instance = new SelfUpdatingValue<T>(userId, key);
            SettingsService settings = GetService();
            instance.subTask = await CreateSubscription(userId, key, defaultGetter, instance, settings);
            // if instance is already disposed we need to unsubscribe, this may or may not be bullshit
            if (instance.IsDisposed)
                instance.Dispose();
            return instance;
        }

        private static async Task<ChannelMessageQueue> CreateSubscription(string userId, string key, Func<T> defaultGetter, SelfUpdatingValue<T> instance, SettingsService settings)
        {
            var sub = await settings.GetAndSubscribe<T>(userId, key, v =>
            {
                instance.LastUpdateReceived = DateTime.UtcNow;
                if (v == null) // should not be null
                    v = SettingsService.DefaultFor<T>(defaultGetter);
                if (instance.ShouldPreventUpdate?.Invoke(v) ?? false)
                    return;
                instance.OnChange?.Invoke(v);
                instance.Value = v;
                instance.AfterChange?.Invoke(v);
                if (instance.IsDisposed)
                    instance.Dispose();
            }, defaultGetter);
            return sub;
        }

        public static Task<SelfUpdatingValue<T>> CreateNoUpdate(Func<T> valGet)
        {
            return Task.FromResult(CreateNoUpdate(valGet()));
        }
        public static SelfUpdatingValue<T> CreateNoUpdate(T value)
        {
            var instance = new SelfUpdatingValue<T>(null, null);
            instance.Value = value;
            return instance;
        }

        private static SettingsService GetService()
        {
            return DiHandler.ServiceProvider.GetRequiredService<SettingsService>();
        }

        public void Dispose()
        {
            IsDisposed = true;
            subTask?.Unsubscribe();
            subTask = null;
            Value = default;
            AfterChange = null;
            OnChange = null;
            ShouldPreventUpdate = null;
        }

        /// <summary>
        /// Replaces the current value with a new one
        /// </summary>
        /// <param name="newValue"></param>
        /// <returns></returns>
        public async Task Update(T newValue)
        {
            if (UserId == null)
            {
                ShouldPreventUpdate?.Invoke(newValue);
                OnChange?.Invoke(newValue);
                Value = newValue;
                AfterChange?.Invoke(newValue);
                return;
            }
            if (newValue == null)
                throw new ArgumentNullException(nameof(newValue));
            await GetService().UpdateSetting(UserId, Key, newValue);
            await Task.Delay(500); // wait for redis to update
            if (LastUpdateReceived < DateTime.UtcNow.AddSeconds(-5))
            {
                DiHandler.GetService<ILogger<SelfUpdatingValue<T>>>().LogWarning(
                    "SelfUpdatingValue {UserId}/{Key} did not receive an update in the last 5 seconds, this may indicate a desync. Re-subscribing.",
                    UserId, Key);
                // apaarently updating did desync, resubscribe
                subTask?.Unsubscribe();
                subTask = await CreateSubscription(UserId, Key, null, this, GetService());
            }
        }

        /// <summary>
        /// Updates the current value by applying the given action
        /// tires to lock the value
        /// </summary>
        public async Task Update(Action<T> update)
        {
            update(Value);
            await Update(Value);
        }

        /// <summary>
        /// Updates the current value
        /// </summary>
        /// <returns></returns>
        public async Task Update()
        {
            await Update(Value);
        }

        public static implicit operator T(SelfUpdatingValue<T> val) => val == null ? default(T) : val.Value;
    }
}
