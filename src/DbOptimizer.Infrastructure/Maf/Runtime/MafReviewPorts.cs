using DbOptimizer.Infrastructure.Maf.DbConfig;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using Microsoft.Agents.AI.Workflows;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

public static class MafReviewPorts
{
    public static readonly RequestPort SqlReview =
        RequestPort.Create<SqlReviewRequestMessage, SqlReviewResponseMessage>("sql-human-review");

    public static readonly RequestPort ConfigReview =
        RequestPort.Create<ConfigReviewRequestMessage, ConfigReviewDecisionResponseMessage>("config-human-review");
}
