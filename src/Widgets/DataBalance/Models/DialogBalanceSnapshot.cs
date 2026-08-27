using System;
using System.Collections.Generic;

namespace DataBalance.Models;

public sealed record DialogBalanceSnapshot(
    DateTime UpdatedAt,
    string Status,
    string ConnectionName,
    string Msisdn,
    string PrepaidBalance,
    string Validity,
    IReadOnlyList<DialogPackageSnapshot> Packages);

public sealed record DialogPackageSnapshot(
    string Type,
    string Code,
    string Name,
    string Total,
    string Remaining,
    string RemainingText,
    string Expiry,
    int Percentage,
    bool HasProgress,
    int Order);