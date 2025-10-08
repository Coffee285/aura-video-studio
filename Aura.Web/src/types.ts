// Type definitions for Aura Video Studio

export interface HardwareCapabilities {
  tier: string;
  cpu: {
    cores: number;
    threads: number;
  };
  ram: {
    gb: number;
  };
  gpu?: {
    model: string;
    vramGB: number;
    vendor: string;
  };
  enableNVENC: boolean;
  enableSD: boolean;
  offlineOnly: boolean;
}

export interface RenderJob {
  id: string;
  status: string;
  progress: number;
  outputPath: string | null;
  createdAt: string;
}

export interface Profile {
  name: string;
  description: string;
}

export interface DownloadItem {
  name: string;
  version: string;
  url: string;
  sha256: string;
  sizeBytes: number;
  installPath: string;
  required: boolean;
}

export interface Brief {
  topic: string;
  audience: string;
  goal: string;
  tone: string;
  language: string;
  aspect: '16:9' | '9:16' | '1:1';
}

export interface PlanSpec {
  targetDurationMinutes: number;
  pacing: 'Chill' | 'Conversational' | 'Fast';
  density: 'Sparse' | 'Normal' | 'Dense';
  style: string;
}

export interface VoiceSpec {
  voiceName: string;
  rate: number;
  pitch: number;
  pauseStyle: 'Auto' | 'None' | 'Breathier';
}
