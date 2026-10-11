/**
 * FormStateManager - Multi-step form state management
 * Handles form data storage, persistence, and recovery across steps
 */

interface FormData {
  [key: string]: any;
}

type StorageType = 'memory' | 'localStorage' | 'sessionStorage';

class FormStateManager {
  private state: FormData = {};
  private storageType: StorageType = 'memory';
  private storageKey: string = 'form-state';
  private history: FormData[] = [];
  private maxHistorySize: number = 50;
  private onStateChange?: (state: FormData) => void;
  private isDirty: boolean = false;

  constructor(storageType: StorageType = 'memory', storageKey: string = 'form-state') {
    this.storageType = storageType;
    this.storageKey = storageKey;
  }

  /**
   * Set a single state value
   */
  setState(key: string, value: any): void {
    if (this.state[key] !== value) {
      this.state[key] = value;
      this.isDirty = true;
      this.onStateChange?.(this.state);
    }
  }

  /**
   * Get a single state value
   */
  getState(key: string): any {
    return this.state[key];
  }

  /**
   * Get all state
   */
  getAllState(): FormData {
    return { ...this.state };
  }

  /**
   * Set multiple state values at once
   */
  setMultipleState(data: FormData): void {
    let hasChanges = false;

    for (const [key, value] of Object.entries(data)) {
      if (this.state[key] !== value) {
        this.state[key] = value;
        hasChanges = true;
      }
    }

    if (hasChanges) {
      this.isDirty = true;
      this.onStateChange?.(this.state);
    }
  }

  /**
   * Merge state data
   */
  mergeState(data: FormData): void {
    this.setMultipleState(data);
  }

  /**
   * Check if a key exists in state
   */
  hasState(key: string): boolean {
    return key in this.state;
  }

  /**
   * Clear a specific state key
   */
  clearStateKey(key: string): void {
    if (key in this.state) {
      delete this.state[key];
      this.isDirty = true;
      this.onStateChange?.(this.state);
    }
  }

  /**
   * Clear all state
   */
  clearState(): void {
    this.state = {};
    this.isDirty = true;
    this.onStateChange?.(this.state);
  }

  /**
   * Save state to storage (localStorage or sessionStorage)
   */
  persistToStorage(key?: string): boolean {
    const storageKey = key || this.storageKey;

    try {
      if (this.storageType === 'memory') {
        return true;
      }

      const storage = this.storageType === 'localStorage' ? localStorage : sessionStorage;
      const serialized = JSON.stringify(this.state);
      storage.setItem(storageKey, serialized);

      this.isDirty = false;
      return true;
    } catch (error) {
      console.error(`Failed to persist state to ${this.storageType}:`, error);
      return false;
    }
  }

  /**
   * Load state from storage
   */
  loadFromStorage(key?: string): boolean {
    const storageKey = key || this.storageKey;

    try {
      if (this.storageType === 'memory') {
        return true;
      }

      const storage = this.storageType === 'localStorage' ? localStorage : sessionStorage;
      const stored = storage.getItem(storageKey);

      if (stored) {
        this.state = JSON.parse(stored);
        this.isDirty = false;
        return true;
      }

      return false;
    } catch (error) {
      console.error(`Failed to load state from ${this.storageType}:`, error);
      return false;
    }
  }

  /**
   * Clear state from storage
   */
  clearFromStorage(key?: string): void {
    const storageKey = key || this.storageKey;

    try {
      if (this.storageType === 'localStorage') {
        localStorage.removeItem(storageKey);
      } else if (this.storageType === 'sessionStorage') {
        sessionStorage.removeItem(storageKey);
      }
    } catch (error) {
      console.error(`Failed to clear state from ${this.storageType}:`, error);
    }
  }

  /**
   * Get state as JSON string
   */
  toJSON(): string {
    return JSON.stringify(this.state);
  }

  /**
   * Load state from JSON string
   */
  fromJSON(jsonString: string): boolean {
    try {
      this.state = JSON.parse(jsonString);
      this.isDirty = true;
      this.onStateChange?.(this.state);
      return true;
    } catch (error) {
      console.error('Failed to parse JSON:', error);
      return false;
    }
  }

  /**
   * Save state to history
   */
  saveToHistory(): void {
    const snapshot = { ...this.state };
    this.history.push(snapshot);

    // Limit history size
    if (this.history.length > this.maxHistorySize) {
      this.history.shift();
    }
  }

  /**
   * Undo to previous state
   */
  undo(): boolean {
    if (this.history.length > 0) {
      this.state = { ...this.history.pop() };
      this.isDirty = true;
      this.onStateChange?.(this.state);
      return true;
    }
    return false;
  }

  /**
   * Check if undo is available
   */
  canUndo(): boolean {
    return this.history.length > 0;
  }

  /**
   * Get history count
   */
  getHistoryCount(): number {
    return this.history.length;
  }

  /**
   * Clear history
   */
  clearHistory(): void {
    this.history = [];
  }

  /**
   * Check if state has been modified
   */
  isDirtyState(): boolean {
    return this.isDirty;
  }

  /**
   * Mark state as clean (persisted)
   */
  markClean(): void {
    this.isDirty = false;
  }

  /**
   * Watch for state changes
   */
  onStateChangeCallback(callback: (state: FormData) => void): void {
    this.onStateChange = callback;
  }

  /**
   * Get field value with default
   */
  getStateWithDefault(key: string, defaultValue: any = ''): any {
    return this.state[key] ?? defaultValue;
  }

  /**
   * Validate state structure
   */
  validateStructure(requiredKeys: string[]): { valid: boolean; missing: string[] } {
    const missing = requiredKeys.filter(key => !(key in this.state));
    return {
      valid: missing.length === 0,
      missing
    };
  }

  /**
   * Get state size in bytes
   */
  getStateSize(): number {
    return new Blob([JSON.stringify(this.state)]).size;
  }

  /**
   * Clone state
   */
  clone(): FormStateManager {
    const clone = new FormStateManager(this.storageType, this.storageKey);
    clone.state = { ...this.state };
    clone.history = this.history.map(h => ({ ...h }));
    return clone;
  }

  /**
   * Sync state with form element
   */
  syncWithForm(formElement: HTMLFormElement): void {
    const formData = new FormData(formElement);

    for (const [key, value] of formData.entries()) {
      this.setState(key, value);
    }
  }

  /**
   * Apply state to form element
   */
  applyToForm(formElement: HTMLFormElement): void {
    const inputs = formElement.querySelectorAll('input, select, textarea');

    inputs.forEach((input: HTMLElement) => {
      const fieldName = (input as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).name;
      if (fieldName in this.state) {
        const element = input as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
        if (element.type === 'checkbox' || element.type === 'radio') {
          element.checked = this.state[fieldName];
        } else {
          element.value = this.state[fieldName];
        }
      }
    });
  }

  /**
   * Export state as CSV (for simple flat data)
   */
  exportAsCSV(): string {
    const headers = Object.keys(this.state);
    const values = headers.map(key => {
      const val = this.state[key];
      return typeof val === 'string' && val.includes(',') ? `"${val}"` : val;
    });

    return [headers.join(','), values.join(',')].join('\n');
  }

  /**
   * Import state from CSV (simple flat data)
   */
  importFromCSV(csvString: string): boolean {
    try {
      const lines = csvString.trim().split('\n');
      if (lines.length < 2) return false;

      const headers = lines[0].split(',');
      const values = lines[1].split(',');

      if (headers.length !== values.length) return false;

      for (let i = 0; i < headers.length; i++) {
        this.setState(headers[i], values[i]);
      }

      return true;
    } catch (error) {
      console.error('Failed to import CSV:', error);
      return false;
    }
  }
}

export { FormStateManager, FormData, StorageType };
