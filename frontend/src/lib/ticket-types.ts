export type TicketPriority = "critical" | "high" | "normal";
export type TicketSubtask = { title: string; description: string; externalId?: string };
export type TicketAttachment = { name: string; contentType: string; data: string; uploaded?: boolean };
export type TicketDraft = { title: string; description: string; type: string; priority: TicketPriority; dueDate: string; labels: string[]; confidence: number; questions: string[]; subTasks: TicketSubtask[] };
