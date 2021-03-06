using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Coflnet.Sky.Commands.Shared
{
    [DataContract]
    public class AccountSettings
    {
        [DataMember(Name = "muted")]
        public HashSet<UserMute> MutedUsers = new ();
        [DataMember(Name = "reminders")]
        public List<Reminder> Reminders = new ();
    }
}