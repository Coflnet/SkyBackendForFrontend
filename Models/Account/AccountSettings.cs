using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Coflnet.Sky.Commands.Shared;
[DataContract]
public class AccountSettings
{
    [DataMember(Name = "muted")]
    public HashSet<UserMute> MutedUsers = new();
    [DataMember(Name = "reminders")]
    public List<Reminder> Reminders = new();
    [DataMember(Name = "loadedConfig")]
    public OwnedConfigs.OwnedConfig LoadedConfig;
    [DataMember(Name = "baseConfigVersion")]
    public int BaseConfigVersion;
    [DataMember(Name = "autoUpdateConfig")]
    public bool AutoUpdateConfig = true;
    [DataMember(Name = "blockLowballs")]
    public bool BlockLowballs = false;
}
