/**
 * The JSON the RagSystem API sends, written as TypeScript types, one per C# record. The mock backend returns exactly
 * these shapes (and the specs read them with the app's own readers), so a change to a record in the API shows up here
 * first. System.Text.Json writes properties in camelCase and enums as the strings listed below; dates are ISO 8601.
 */

/** src/RagSystem/Chat/Dtos/ChatRequest.cs */
export interface ChatRequestDto {
  conversationId?: string | null;
  message?: string | null;
}

/** ChatCitation: Title, Part (the first part is 1), Text ("title, part N"). */
export interface ChatCitationDto {
  title: string;
  part: number;
  text: string;
}

/** ChatUsage. All 0 when no model was called. */
export interface ChatUsageDto {
  inputTokens: number;
  outputTokens: number;
  thinkingTokens: number;
  cacheHit: boolean;
}

/** ChatResponse */
export interface ChatResponseDto {
  conversationId: string;
  messageId: string;
  answer: string;
  citations: ChatCitationDto[];
  usage: ChatUsageDto;
}

/** MessageView: citations are empty and usage is null on a question. */
export interface MessageViewDto {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  citations: ChatCitationDto[];
  usage: ChatUsageDto | null;
  createdAt: string;
}

/** src/RagSystem/Documents/Dtos/DocumentView.cs. Status is DocumentStatus.ToString(). */
export interface DocumentViewDto {
  id: string;
  version: string;
  fileName: string;
  title: string;
  status: 'Processing' | 'Ready' | 'Failed';
  chunkCount: number;
  error: string | null;
  uploadedAt: string;
  readyAt: string | null;
}

/** ChunkView: the vector is left out. */
export interface ChunkViewDto {
  id: string;
  ordinal: number;
  content: string;
  estTokens: number;
  embeddingModel: string;
}

/** AcceptedDocument (202) */
export interface AcceptedDocumentDto {
  id: string;
  status: 'Processing';
}

export interface IssueReportDto {
  code: string;
  message: string;
  line: number | null;
}

export interface ChunkReportDto {
  ordinal: number;
  text: string;
  estTokens: number;
}

/** DryRunReport: the answer of a dry run (200) and of a rejected upload (422, with no chunks). */
export interface DryRunReportDto {
  fileName: string;
  version: string;
  title: string;
  valid: boolean;
  errors: IssueReportDto[];
  warnings: IssueReportDto[];
  chunks: ChunkReportDto[];
}

/** src/RagSystem/Retrieval/Dtos/SearchResponse.cs */
export interface SearchSourceDto {
  label: string;
  citation: string;
  score: number;
  estTokens: number;
  chunkId: string;
  content: string;
}

export interface SearchDroppedDto {
  citation: string;
  score: number;
  reason: 'BelowMinScore' | 'NearDuplicate' | 'OverBudget';
  chunkId: string;
}

export interface SearchResponseDto {
  version: string;
  question: string;
  candidates: number;
  sources: SearchSourceDto[];
  dropped: SearchDroppedDto[];
}

/** application/problem+json from Http/ProblemResults.cs: the standard fields plus `reason`. */
export interface ProblemDto {
  title: string;
  status: number;
  detail: string;
  reason: string;
}
