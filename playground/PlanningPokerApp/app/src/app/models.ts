export interface PokerSession {
  id: string;
  name: string;
  currentStory: string;
  isRevealed: boolean;
  createdAt: string;
  votes: Vote[];
}

export interface Vote {
  participantName: string;
  value: string;
  votedAt: string;
}

export interface CreateSessionRequest {
  name: string;
}

export interface SubmitVoteRequest {
  participantName: string;
  value: string;
}
