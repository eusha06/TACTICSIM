using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TacticSim.Web.Data;

namespace TacticSim.Web.Services;

// One-time operator command when moving an existing filesystem key ring.
// It only adds missing XML records; never removes or replaces any key.
public static class KeyRingImport
{
    public static async Task<int> Import(MatchDbContext db, string directory)
    {
        var files = Directory.GetFiles(directory, "*.xml");
        if (files.Length == 0) throw new InvalidOperationException("The key directory contains no XML files.");
        var entries = files.Select(file => (Name: Path.GetFileNameWithoutExtension(file),
            Xml: XElement.Load(file).ToString(SaveOptions.DisableFormatting))).ToList();
        if (entries.Any(entry => XElement.Parse(entry.Xml).Name.LocalName is not ("key" or "revocation")))
            throw new InvalidOperationException("The directory must contain only Data Protection key/revocation XML files.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var existing = (await db.DataProtectionKeys.Select(key => key.Xml).ToListAsync())
            .Select(xml => XElement.Parse(xml!).ToString(SaveOptions.DisableFormatting)).ToHashSet();
        var added = 0;
        foreach (var entry in entries)
        {
            if (!existing.Add(entry.Xml)) continue;
            db.DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = entry.Name, Xml = entry.Xml });
            added++;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return added;
    }
}
