using System.ComponentModel.DataAnnotations;

namespace BTCPayApp.Core.Data;

public class Setting
{
    [Key]
    public required string Key { get; set; }
    public byte[]? Value { get; set; }
}
