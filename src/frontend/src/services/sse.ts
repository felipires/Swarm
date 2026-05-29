export interface LogMessage {
  id: string;
  timestamp: string;
  level: "INFO" | "WARNING" | "ERROR" | "CRITICAL";
  message: string;
  phase?: string;
}

export class SseClient {
  private eventSource: EventSource | null = null;

  connect(runId: string, onMessage: (log: LogMessage) => void, onError: (error: Event) => void) {
    const baseUrl = (import.meta as any).env.VITE_API_URL || "http://localhost:5001/api";

    this.eventSource = new EventSource(`${baseUrl}/runs/${runId}/stream`);

    this.eventSource.addEventListener("message", (event: MessageEvent) => {
      try {
        const log = JSON.parse(event.data) as LogMessage;
        onMessage(log);
      } catch (e) {
        console.error("Failed to parse log message", e);
      }
    });

    this.eventSource.addEventListener("error", onError);
  }

  disconnect() {
    if (this.eventSource) {
      this.eventSource.close();
      this.eventSource = null;
    }
  }

  isConnected(): boolean {
    return this.eventSource !== null && this.eventSource.readyState === EventSource.OPEN;
  }
}

export const sseClient = new SseClient();
