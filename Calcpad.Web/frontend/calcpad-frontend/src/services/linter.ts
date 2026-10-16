import type { CalcpadApiClient } from '../api/client';
import type { LintResponse, LintDiagnostic } from '../types/api';
import type { ILogger } from '../types/interfaces';
import { truncateBase64Content } from './base64-truncate';

/**
 * Platform-agnostic linting service that fetches diagnostics from the server and returns raw
 * LintDiagnostic[]. Consumers wrap these into their platform-specific diagnostic types.
 */
export class CalcpadLintService {
    private apiClient: CalcpadApiClient;
    private logger?: ILogger;
    private requestId = 0;

    constructor(apiClient: CalcpadApiClient, logger?: ILogger) {
        this.apiClient = apiClient;
        this.logger = logger;
    }

    /**
     * Lint content and return raw diagnostics from the server.
     */
    public async lintContent(
        content: string,
        sourceFilePath?: string,
        key?: string,
    ): Promise<LintResponse | null> {
        const reqId = ++this.requestId;
        const startTime = Date.now();

        this.logger?.appendLine(`[Lint #${reqId}] Request started (${content.length} chars)`);

        if (!content.trim()) {
            this.logger?.appendLine(`[Lint #${reqId}] Skipped - empty content`);
            return null;
        }

        const truncated = truncateBase64Content(content);
        const response = await this.apiClient.lint(truncated, sourceFilePath, { key });

        if (response) {
            this.logger?.appendLine(
                `[Lint #${reqId}] Found ${response.errorCount} errors, ${response.warningCount} warnings in ${Date.now() - startTime}ms`
            );
        } else {
            this.logger?.appendLine(`[Lint #${reqId}] No response from server after ${Date.now() - startTime}ms`);
        }

        return response;
    }

    /**
     * Filter diagnostics by minimum severity.
     * severityId: 0=Error, 1=Warning
     * Returns diagnostics at or above the minimum severity (lower number = higher severity).
     */
    public static filterBySeverity(
        diagnostics: LintDiagnostic[],
        minimumSeverity: 'error' | 'warning' | 'information'
    ): LintDiagnostic[] {
        switch (minimumSeverity) {
            case 'error':
                return diagnostics.filter(d => d.severityId === 0);
            case 'warning':
                return diagnostics.filter(d => d.severityId <= 1);
            default:
                return diagnostics;
        }
    }
}
