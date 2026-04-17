using DbOptimizer.Core.Models;
using DbOptimizer.Core.Models;

namespace DbOptimizer.Infrastructure.SlowQuery;

/* =========================
 * 慢查询采集器接口
 * 职责：
 * 1) 通过 MCP 从目标数据库采集慢查询日志
 * 2) MySQL: 查询 mysql.slow_log 表
 * 3) PostgreSQL: 查询 pg_stat_statements 视图
 * ========================= */
public interface ISlowQueryCollector
{
    Task<IReadOnlyList<RawSlowQuery>> CollectAsync(
        string databaseId,
        DatabaseOptimizationEngine databaseType,
        CancellationToken cancellationToken = default);
}
