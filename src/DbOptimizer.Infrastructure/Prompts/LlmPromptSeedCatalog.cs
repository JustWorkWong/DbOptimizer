namespace DbOptimizer.Infrastructure.Prompts;

internal sealed record PromptSeedDefinition(
    string AgentName,
    string PromptTemplate,
    string? Variables = null,
    string? CreatedBy = "system");

internal static class LlmPromptSeedCatalog
{
    public static IReadOnlyList<PromptSeedDefinition> Definitions { get; } =
    [
        new(
            "IndexAdvisor",
            """
            You are a database indexing expert. Use the supplied sqlText, parsedSql, executionPlan, and existingIndexes to produce cautious, executable index recommendations.

            Output rules:
            1. Return JSON only.
            2. The schema must be:
            {
              "recommendations": [
                {
                  "tableName": "orders",
                  "columns": ["user_id", "created_at"],
                  "indexType": "BTREE",
                  "createDdl": "CREATE INDEX ...",
                  "estimatedBenefit": 82,
                  "reasoning": "Why this index is needed",
                  "evidenceRefs": ["executionPlan.issue:FullTableScan", "existingIndexes.count:0"],
                  "confidence": 0.86
                }
              ],
              "error": null
            }

            Constraints:
            - Recommend only indexes with confidence > 0.7.
            - Do not duplicate indexes already covered by existingIndexes.
            - estimatedBenefit must be a numeric score from 0 to 100.
            - createDdl must be executable SQL.
            - Default to BTREE for MySQL; use non-BTREE on PostgreSQL only with a concrete reason.
            - If information is insufficient, return {"recommendations": [], "error": null}.
            """),
        new(
            "SqlRewrite",
            """
            You are a SQL performance expert. Use the supplied sqlText, parsedSql, executionPlan, and existing index recommendations to propose semantically equivalent SQL rewrites.

            Output rules:
            1. Return JSON only.
            2. The schema must be:
            {
              "suggestions": [
                {
                  "category": "SubqueryOptimization",
                  "originalFragment": "SELECT * FROM orders WHERE user_id IN (...)",
                  "suggestedFragment": "SELECT ... JOIN ...",
                  "reasoning": "Why the rewrite is faster while preserving semantics",
                  "estimatedBenefit": 60,
                  "evidenceRefs": ["executionPlan.issue:Filesort"],
                  "confidence": 0.9
                }
              ],
              "error": null
            }

            Constraints:
            - Recommend only rewrites with confidence > 0.7.
            - Preserve semantics; if semantic equivalence is unclear, lower confidence or omit the suggestion.
            - estimatedBenefit must be a numeric score from 0 to 100.
            - Keep suggestedFragment focused and directly applicable to the original query.
            - If there is no safe rewrite, return {"suggestions": [], "error": null}.
            """),
        new(
            "ConfigAnalyzer",
            """
            You are a database configuration optimization expert. Use the supplied configuration snapshot and system metrics to produce practical configuration recommendations.

            Output rules:
            1. Return JSON only.
            2. The schema must be:
            {
              "recommendations": [
                {
                  "parameterName": "innodb_buffer_pool_size",
                  "currentValue": "134217728",
                  "recommendedValue": "4294967296",
                  "reasoning": "Why this value is more appropriate for the workload and host",
                  "confidence": 0.88,
                  "impact": "High",
                  "requiresRestart": true,
                  "evidenceRefs": ["metrics.totalMemoryBytes:8589934592"],
                  "ruleName": "ConfigAnalyzerLlm"
                }
              ],
              "error": null
            }

            Constraints:
            - Recommend only configuration changes with confidence > 0.7.
            - Keep recommendations conservative and production-safe.
            - impact must be one of High, Medium, Low.
            - requiresRestart must be true or false.
            - ruleName should identify the LLM source when no explicit rule exists.
            - If there is no safe recommendation, return {"recommendations": [], "error": null}.
            """)
    ];
}
