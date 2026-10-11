/**
 * FormDraftHandler - Draft management for form progress saving
 * Auto-save, draft versioning, and recovery
 */

export interface FormDraft {
  id: string;
  formId: string;
  userId: string;
  data: any;
  currentStep: number;
  completedSteps: number[];
  savedAt: number;
  expiresAt: number;
  version: number;
  status: 'in_progress' | 'abandoned' | 'submitted';
  notes?: string;
}

export interface DraftVersion {
  version: number;
  data: any;
  savedAt: number;
  description?: string;
}

export interface DraftMetadata {
  draftId: string;
  formId: string;
  lastSaved: number;
  progress: number;
  versionCount: number;
}

export class FormDraftHandler {
  private drafts: Map<string, FormDraft>;
  private versions: Map<string, DraftVersion[]>;
  private autoSaveInterval: NodeJS.Timeout | null = null;
  private autoSaveDelay = 30000; // 30 seconds
  private pendingAutoSave: Map<string, any>;
  private maxVersions = 10;
  private draftExpiryTime = 30 * 24 * 60 * 60 * 1000; // 30 days
  private onAutoSave: ((draft: FormDraft) => void) | null = null;
  private storageKey = 'form_drafts';

  constructor() {
    this.drafts = new Map();
    this.versions = new Map();
    this.pendingAutoSave = new Map();
    this.loadFromStorage();
    this.startAutoSaveCheck();
  }

  /**
   * Create or get draft
   */
  getDraft(formId: string, userId: string): FormDraft {
    const draftId = `draft_${formId}_${userId}`;
    let draft = this.drafts.get(draftId);

    if (!draft) {
      draft = {
        id: draftId,
        formId,
        userId,
        data: {},
        currentStep: 0,
        completedSteps: [],
        savedAt: Date.now(),
        expiresAt: Date.now() + this.draftExpiryTime,
        version: 1,
        status: 'in_progress'
      };

      this.drafts.set(draftId, draft);
      this.versions.set(draftId, []);
    }

    return draft;
  }

  /**
   * Save draft
   */
  saveDraft(
    formId: string,
    userId: string,
    data: any,
    options?: { currentStep?: number; completedSteps?: number[]; notes?: string }
  ): FormDraft {
    const draftId = `draft_${formId}_${userId}`;
    let draft = this.drafts.get(draftId);

    if (!draft) {
      draft = this.getDraft(formId, userId);
    }

    // Create version
    const version: DraftVersion = {
      version: draft.version,
      data: JSON.parse(JSON.stringify(data)),
      savedAt: Date.now(),
      description: options?.notes
    };

    if (!this.versions.has(draftId)) {
      this.versions.set(draftId, []);
    }

    const versionHistory = this.versions.get(draftId)!;
    versionHistory.push(version);

    // Limit version history
    if (versionHistory.length > this.maxVersions) {
      versionHistory.shift();
    }

    // Update draft
    draft = {
      ...draft,
      data: JSON.parse(JSON.stringify(data)),
      currentStep: options?.currentStep ?? draft.currentStep,
      completedSteps: options?.completedSteps ?? draft.completedSteps,
      savedAt: Date.now(),
      expiresAt: Date.now() + this.draftExpiryTime,
      version: draft.version + 1,
      notes: options?.notes
    };

    this.drafts.set(draftId, draft);
    this.pendingAutoSave.delete(draftId);
    this.persistToStorage();

    return draft;
  }

  /**
   * Auto-save draft
   */
  autoSave(
    formId: string,
    userId: string,
    data: any,
    options?: { currentStep?: number; completedSteps?: number[] }
  ): void {
    const draftId = `draft_${formId}_${userId}`;

    // Queue auto-save
    this.pendingAutoSave.set(draftId, {
      formId,
      userId,
      data,
      options
    });
  }

  /**
   * Load draft
   */
  loadDraft(formId: string, userId: string): FormDraft | null {
    const draftId = `draft_${formId}_${userId}`;
    const draft = this.drafts.get(draftId);

    if (!draft) {
      return null;
    }

    // Check expiry
    if (Date.now() > draft.expiresAt) {
      this.deleteDraft(formId, userId);
      return null;
    }

    return draft;
  }

  /**
   * List user's drafts
   */
  listDrafts(userId: string): FormDraft[] {
    const userDrafts: FormDraft[] = [];

    for (const draft of this.drafts.values()) {
      if (draft.userId === userId && Date.now() <= draft.expiresAt) {
        userDrafts.push(draft);
      }
    }

    return userDrafts;
  }

  /**
   * Delete draft
   */
  deleteDraft(formId: string, userId: string): boolean {
    const draftId = `draft_${formId}_${userId}`;

    if (!this.drafts.has(draftId)) {
      return false;
    }

    this.drafts.delete(draftId);
    this.versions.delete(draftId);
    this.persistToStorage();

    return true;
  }

  /**
   * Get draft version
   */
  getVersion(formId: string, userId: string, versionNumber: number): any | null {
    const draftId = `draft_${formId}_${userId}`;
    const versionHistory = this.versions.get(draftId);

    if (!versionHistory) {
      return null;
    }

    const version = versionHistory.find(v => v.version === versionNumber);
    return version?.data || null;
  }

  /**
   * Get all versions
   */
  getVersionHistory(formId: string, userId: string): DraftVersion[] {
    const draftId = `draft_${formId}_${userId}`;
    const versionHistory = this.versions.get(draftId) || [];

    return [...versionHistory];
  }

  /**
   * Restore version
   */
  restoreVersion(
    formId: string,
    userId: string,
    versionNumber: number
  ): FormDraft | null {
    const draftId = `draft_${formId}_${userId}`;
    const versionHistory = this.versions.get(draftId);

    if (!versionHistory) {
      return null;
    }

    const version = versionHistory.find(v => v.version === versionNumber);
    if (!version) {
      return null;
    }

    return this.saveDraft(formId, userId, version.data, {
      notes: `Restored from version ${versionNumber}`
    });
  }

  /**
   * Mark draft as submitted
   */
  markSubmitted(formId: string, userId: string): FormDraft | null {
    const draftId = `draft_${formId}_${userId}`;
    const draft = this.drafts.get(draftId);

    if (!draft) {
      return null;
    }

    draft.status = 'submitted';
    draft.expiresAt = Date.now() + 7 * 24 * 60 * 60 * 1000; // Keep for 7 days after submit

    this.persistToStorage();
    return draft;
  }

  /**
   * Mark draft as abandoned
   */
  markAbandoned(formId: string, userId: string): FormDraft | null {
    const draftId = `draft_${formId}_${userId}`;
    const draft = this.drafts.get(draftId);

    if (!draft) {
      return null;
    }

    draft.status = 'abandoned';
    this.persistToStorage();

    return draft;
  }

  /**
   * Get draft metadata
   */
  getDraftMetadata(formId: string, userId: string): DraftMetadata | null {
    const draft = this.loadDraft(formId, userId);

    if (!draft) {
      return null;
    }

    const versionCount = (this.versions.get(draft.id) || []).length;

    return {
      draftId: draft.id,
      formId,
      lastSaved: draft.savedAt,
      progress: draft.completedSteps.length,
      versionCount
    };
  }

  /**
   * Set auto-save callback
   */
  onAutoSaved(callback: (draft: FormDraft) => void): FormDraftHandler {
    this.onAutoSave = callback;
    return this;
  }

  /**
   * Cleanup expired drafts
   */
  cleanupExpired(): number {
    let cleaned = 0;
    const now = Date.now();

    for (const [draftId, draft] of this.drafts.entries()) {
      if (now > draft.expiresAt) {
        this.drafts.delete(draftId);
        this.versions.delete(draftId);
        cleaned++;
      }
    }

    if (cleaned > 0) {
      this.persistToStorage();
    }

    return cleaned;
  }

  /**
   * Get storage size
   */
  getStorageSize(): number {
    const data = JSON.stringify({
      drafts: Array.from(this.drafts.values()),
      versions: Object.fromEntries(this.versions)
    });

    return new Blob([data]).size;
  }

  /**
   * Export draft as JSON
   */
  exportDraft(formId: string, userId: string): string | null {
    const draft = this.loadDraft(formId, userId);

    if (!draft) {
      return null;
    }

    return JSON.stringify(draft, null, 2);
  }

  /**
   * Import draft from JSON
   */
  importDraft(json: string): FormDraft | null {
    try {
      const data = JSON.parse(json);

      if (!data.formId || !data.userId) {
        throw new Error('Invalid draft structure');
      }

      const draft: FormDraft = {
        ...data,
        expiresAt: Date.now() + this.draftExpiryTime,
        savedAt: Date.now()
      };

      this.drafts.set(draft.id, draft);
      this.persistToStorage();

      return draft;
    } catch (error) {
      console.error('Failed to import draft:', error);
      return null;
    }
  }

  /**
   * Start auto-save check
   */
  private startAutoSaveCheck(): void {
    this.autoSaveInterval = setInterval(() => {
      for (const [draftId, saveData] of this.pendingAutoSave.entries()) {
        const draft = this.saveDraft(
          saveData.formId,
          saveData.userId,
          saveData.data,
          saveData.options
        );

        if (this.onAutoSave) {
          this.onAutoSave(draft);
        }

        this.pendingAutoSave.delete(draftId);
      }
    }, this.autoSaveDelay);
  }

  /**
   * Persist to storage
   */
  private persistToStorage(): void {
    try {
      const data = {
        drafts: Array.from(this.drafts.values()),
        versions: Object.fromEntries(this.versions)
      };

      localStorage.setItem(this.storageKey, JSON.stringify(data));
    } catch (error) {
      console.error('Failed to persist drafts:', error);
    }
  }

  /**
   * Load from storage
   */
  private loadFromStorage(): void {
    try {
      const data = localStorage.getItem(this.storageKey);

      if (!data) {
        return;
      }

      const parsed = JSON.parse(data);

      for (const draft of parsed.drafts || []) {
        this.drafts.set(draft.id, draft);
      }

      for (const [draftId, versionList] of Object.entries(parsed.versions || {})) {
        this.versions.set(draftId, versionList as DraftVersion[]);
      }
    } catch (error) {
      console.error('Failed to load drafts:', error);
    }
  }

  /**
   * Cleanup
   */
  destroy(): void {
    if (this.autoSaveInterval) {
      clearInterval(this.autoSaveInterval);
    }
  }
}
