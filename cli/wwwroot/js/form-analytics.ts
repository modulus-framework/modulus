/**
 * FormAnalytics - Event tracking and analytics for forms
 * Track user interactions, completion rates, and form behavior
 */

export interface AnalyticsEvent {
  eventType: 'form_start' | 'form_step_enter' | 'form_step_exit' | 'field_interact' | 'form_submit' | 'form_abandon' | 'error';
  formId: string;
  userId?: string;
  stepIndex?: number;
  fieldId?: string;
  timestamp: number;
  duration?: number;
  metadata?: Record<string, any>;
}

export interface FormMetrics {
  formId: string;
  totalStarts: number;
  totalSubmissions: number;
  totalAbandonments: number;
  completionRate: number;
  averageTimeSpent: number;
  stepMetrics: StepMetric[];
  fieldMetrics: FieldMetric[];
}

export interface StepMetric {
  stepIndex: number;
  timeEntered: number;
  timeExited: number;
  averageTimeSpent: number;
  abandonmentRate: number;
  completionRate: number;
}

export interface FieldMetric {
  fieldId: string;
  interactionCount: number;
  errorCount: number;
  averageTimeSpent: number;
}

export class FormAnalytics {
  private events: AnalyticsEvent[];
  private sessionData: Map<string, any>;
  private startTime: number;
  private onEventTracked: ((event: AnalyticsEvent) => void) | null = null;
  private maxEvents = 10000;
  private storageKey = 'form_analytics';

  constructor() {
    this.events = [];
    this.sessionData = new Map();
    this.startTime = Date.now();
    this.loadFromStorage();
  }

  /**
   * Track analytics event
   */
  trackEvent(event: Omit<AnalyticsEvent, 'timestamp'>): void {
    const fullEvent: AnalyticsEvent = {
      ...event,
      timestamp: Date.now()
    };

    this.events.push(fullEvent);

    // Limit event storage
    if (this.events.length > this.maxEvents) {
      this.events.shift();
    }

    this.persistToStorage();

    if (this.onEventTracked) {
      this.onEventTracked(fullEvent);
    }
  }

  /**
   * Track form start
   */
  trackFormStart(formId: string, userId?: string): void {
    this.trackEvent({
      eventType: 'form_start',
      formId,
      userId,
      metadata: { sessionId: this.getSessionId() }
    });

    this.sessionData.set(`${formId}_${userId}_start`, Date.now());
  }

  /**
   * Track step entry
   */
  trackStepEnter(formId: string, stepIndex: number, userId?: string): void {
    this.trackEvent({
      eventType: 'form_step_enter',
      formId,
      stepIndex,
      userId
    });

    this.sessionData.set(`step_${stepIndex}_enter`, Date.now());
  }

  /**
   * Track step exit
   */
  trackStepExit(formId: string, stepIndex: number, userId?: string): void {
    const enterTime = this.sessionData.get(`step_${stepIndex}_enter`);
    const duration = enterTime ? Date.now() - enterTime : undefined;

    this.trackEvent({
      eventType: 'form_step_exit',
      formId,
      stepIndex,
      userId,
      duration
    });
  }

  /**
   * Track field interaction
   */
  trackFieldInteraction(formId: string, fieldId: string, userId?: string, action?: string): void {
    this.trackEvent({
      eventType: 'field_interact',
      formId,
      fieldId,
      userId,
      metadata: { action }
    });
  }

  /**
   * Track form submission
   */
  trackFormSubmit(formId: string, userId?: string, data?: any): void {
    const startTime = this.sessionData.get(`${formId}_${userId}_start`);
    const duration = startTime ? Date.now() - startTime : undefined;

    this.trackEvent({
      eventType: 'form_submit',
      formId,
      userId,
      duration,
      metadata: { dataSize: data ? JSON.stringify(data).length : 0 }
    });
  }

  /**
   * Track form abandonment
   */
  trackFormAbandon(formId: string, userId?: string, lastStep?: number): void {
    const startTime = this.sessionData.get(`${formId}_${userId}_start`);
    const duration = startTime ? Date.now() - startTime : undefined;

    this.trackEvent({
      eventType: 'form_abandon',
      formId,
      userId,
      stepIndex: lastStep,
      duration
    });
  }

  /**
   * Track error
   */
  trackError(formId: string, fieldId?: string, errorMessage?: string, userId?: string): void {
    this.trackEvent({
      eventType: 'error',
      formId,
      fieldId,
      userId,
      metadata: { error: errorMessage }
    });
  }

  /**
   * Get form metrics
   */
  getFormMetrics(formId: string): FormMetrics {
    const formEvents = this.events.filter(e => e.formId === formId);

    const starts = formEvents.filter(e => e.eventType === 'form_start').length;
    const submissions = formEvents.filter(e => e.eventType === 'form_submit').length;
    const abandonments = formEvents.filter(e => e.eventType === 'form_abandon').length;

    const completionRate = starts > 0 ? (submissions / starts) * 100 : 0;

    const averageTime = this.calculateAverageTime(formEvents);

    return {
      formId,
      totalStarts: starts,
      totalSubmissions: submissions,
      totalAbandonments: abandonments,
      completionRate,
      averageTimeSpent: averageTime,
      stepMetrics: this.calculateStepMetrics(formId),
      fieldMetrics: this.calculateFieldMetrics(formId)
    };
  }

  /**
   * Get completion rate
   */
  getCompletionRate(formId: string): number {
    const metrics = this.getFormMetrics(formId);
    return metrics.completionRate;
  }

  /**
   * Get average time spent
   */
  getAverageTime(formId: string): number {
    const metrics = this.getFormMetrics(formId);
    return metrics.averageTimeSpent;
  }

  /**
   * Get step analytics
   */
  getStepAnalytics(formId: string, stepIndex: number): StepMetric | null {
    const metrics = this.getFormMetrics(formId);
    return metrics.stepMetrics.find(m => m.stepIndex === stepIndex) || null;
  }

  /**
   * Get field analytics
   */
  getFieldAnalytics(formId: string, fieldId: string): FieldMetric | null {
    const metrics = this.getFormMetrics(formId);
    return metrics.fieldMetrics.find(m => m.fieldId === fieldId) || null;
  }

  /**
   * Export analytics
   */
  exportAnalytics(formId?: string): string {
    const eventsToExport = formId
      ? this.events.filter(e => e.formId === formId)
      : this.events;

    return JSON.stringify(eventsToExport, null, 2);
  }

  /**
   * Export metrics
   */
  exportMetrics(formId: string): string {
    const metrics = this.getFormMetrics(formId);
    return JSON.stringify(metrics, null, 2);
  }

  /**
   * Get all events
   */
  getAllEvents(formId?: string): AnalyticsEvent[] {
    return formId
      ? this.events.filter(e => e.formId === formId)
      : [...this.events];
  }

  /**
   * Set event callback
   */
  onEventTracked(callback: (event: AnalyticsEvent) => void): FormAnalytics {
    this.onEventTracked = callback;
    return this;
  }

  /**
   * Calculate average time
   */
  private calculateAverageTime(events: AnalyticsEvent[]): number {
    const durations = events
      .filter(e => e.duration !== undefined)
      .map(e => e.duration!);

    if (durations.length === 0) return 0;

    const sum = durations.reduce((a, b) => a + b, 0);
    return sum / durations.length;
  }

  /**
   * Calculate step metrics
   */
  private calculateStepMetrics(formId: string): StepMetric[] {
    const stepMetrics = new Map<number, StepMetric>();

    const formEvents = this.events.filter(e => e.formId === formId);

    for (const event of formEvents) {
      if (event.stepIndex === undefined) continue;

      if (!stepMetrics.has(event.stepIndex)) {
        stepMetrics.set(event.stepIndex, {
          stepIndex: event.stepIndex,
          timeEntered: 0,
          timeExited: 0,
          averageTimeSpent: 0,
          abandonmentRate: 0,
          completionRate: 0
        });
      }

      const metric = stepMetrics.get(event.stepIndex)!;

      if (event.eventType === 'form_step_enter') {
        metric.timeEntered++;
      } else if (event.eventType === 'form_step_exit') {
        metric.timeExited++;
      }
    }

    return Array.from(stepMetrics.values());
  }

  /**
   * Calculate field metrics
   */
  private calculateFieldMetrics(formId: string): FieldMetric[] {
    const fieldMetrics = new Map<string, FieldMetric>();

    const formEvents = this.events.filter(e => e.formId === formId);

    for (const event of formEvents) {
      if (event.fieldId === undefined) continue;

      if (!fieldMetrics.has(event.fieldId)) {
        fieldMetrics.set(event.fieldId, {
          fieldId: event.fieldId,
          interactionCount: 0,
          errorCount: 0,
          averageTimeSpent: 0
        });
      }

      const metric = fieldMetrics.get(event.fieldId)!;

      if (event.eventType === 'field_interact') {
        metric.interactionCount++;
      } else if (event.eventType === 'error') {
        metric.errorCount++;
      }
    }

    return Array.from(fieldMetrics.values());
  }

  /**
   * Get session ID
   */
  private getSessionId(): string {
    let sessionId = this.sessionData.get('sessionId');

    if (!sessionId) {
      sessionId = `session_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
      this.sessionData.set('sessionId', sessionId);
    }

    return sessionId;
  }

  /**
   * Persist to storage
   */
  private persistToStorage(): void {
    try {
      localStorage.setItem(this.storageKey, JSON.stringify(this.events.slice(-1000)));
    } catch (error) {
      console.error('Failed to persist analytics:', error);
    }
  }

  /**
   * Load from storage
   */
  private loadFromStorage(): void {
    try {
      const data = localStorage.getItem(this.storageKey);
      if (data) {
        this.events = JSON.parse(data);
      }
    } catch (error) {
      console.error('Failed to load analytics:', error);
    }
  }

  /**
   * Clear analytics
   */
  clear(): void {
    this.events = [];
    localStorage.removeItem(this.storageKey);
  }
}
