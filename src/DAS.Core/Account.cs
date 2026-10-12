using System.Security.Cryptography;
using System.Text;

namespace DAS.Core;

public enum AccountStatus
{
    Pending,
    Running,
    Ok,
    BadCred,
    NeedsHuman,
    Blocked,
    Unknown
}

public sealed record Account(string User, string Pass, string? Tag = null)
{
    /// <summary>只落盘哈希，队列明文不离开内存。</summary>
    public string UserHash => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(User.Trim().ToLowerInvariant())))[..16];

    public override string ToString() => $"{User}[{Tag ?? "-"}]";
}
