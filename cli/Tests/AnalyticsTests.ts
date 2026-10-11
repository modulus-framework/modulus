/**
 * AnalyticsTests - Test suite for form analytics and tracking
 */

import { FormAnalytics } from '../wwwroot/js/form-analytics';

describe('FormAnalytics', () => {
  let analytics: FormAnalytics;

  beforeEach(() => {
    analytics = new FormAnalytics();
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
  });

  describe('Event Tracking', () => {
    test('should track form start event', () => {
      analytics.trackFormStart('form1', 'user1');

      const events = analytics.getAllEvents('form1');
      expect(events).toHaveLength(1);
      expect(events[0].eventType).toBe('form_start');
    });

    test('should track step events', () => {
      analytics.trackStepEnter('form1', 0, 'user1');
      analytics.trackStepExit('form1', 0, 'user1');

      const events = analytics.getAllEvents('form1');
      expect(events.length).toBeGreaterThanOrEqual(2);
    });

    test('should track field interactions', () => {
      analytics.trackFieldInteraction('form1', 'field1', 'user1', 'focus');

      const events = analytics.getAllEvents('form1');
      const fieldEvent = events.find(e => e.eventType === 'field_interact');
      expect(fieldEvent).toBeDefined();
    });

    test('should track form submission', () => {
      analytics.trackFormStart('form1', 'user1');
      analytics.trackFormSubmit('form1', 'user1');

      const events = analytics.getAllEvents('form1');
      const submitEvent = events.find(e => e.eventType === 'form_submit');
      expect(submitEvent).toBeDefined();
    });

    test('should track form abandonment', () => {
      analytics.trackFormStart('form1', 'user1');
      analytics.trackFormAbandon('form1', 'user1', 2);

      const events = analytics.getAllEvents('form1');
      const abandonEvent = events.find(e => e.eventType === 'form_abandon');
      expect(abandonEvent).toBeDefined();
    });

    test('should track errors', () => {
      analytics.trackError('form1', 'field1', 'Invalid email', 'user1');

      const events = analytics.getAllEvents('form1');
      const errorEvent = events.find(e => e.eventType === 'error');
      expect(errorEvent).toBeDefined();
    });
  });

  describe('Metrics Calculation', () => {
    test('should calculate completion rate', () => {
      analytics.trackFormStart('form1', 'user1');
      analytics.trackFormStart('form1', 'user2');
      analytics.trackFormSubmit('form1', 'user1');

      const rate = analytics.getCompletionRate('form1');
      expect(rate).toBe(50); // 1 out of 2
    });

    test('should calculate average time spent', () => {
      analytics.trackFormStart('form1', 'user1');
      // Simulate time passage
      analytics.trackFormSubmit('form1', 'user1');

      const avgTime = analytics.getAverageTime('form1');
      expect(typeof avgTime).toBe('number');
    });

    test('should generate form metrics', () => {
      analytics.trackFormStart('form1', 'user1');
      analytics.trackStepEnter('form1', 0, 'user1');
      analytics.trackStepExit('form1', 0, 'user1');
      analytics.trackFieldInteraction('form1', 'field1', 'user1');
      analytics.trackFormSubmit('form1', 'user1');

      const metrics = analytics.getFormMetrics('form1');

      expect(metrics.formId).toBe('form1');
      expect(metrics.totalStarts).toBe(1);
      expect(metrics.totalSubmissions).toBe(1);
      expect(metrics.completionRate).toBe(100);
    });
  });

  describe('Step Analytics', () => {
    test('should get step metrics', () => {
      analytics.trackStepEnter('form1', 0, 'user1');
      analytics.trackStepExit('form1', 0, 'user1');

      const stepMetric = analytics.getStepAnalytics('form1', 0);
      expect(stepMetric).toBeDefined();
      expect(stepMetric?.stepIndex).toBe(0);
    });
  });

  describe('Field Analytics', () => {
    test('should get field metrics', () => {
      analytics.trackFieldInteraction('form1', 'field1', 'user1');
      analytics.trackFieldInteraction('form1', 'field1', 'user1');
      analytics.trackError('form1', 'field1', 'Error', 'user1');

      const fieldMetric = analytics.getFieldAnalytics('form1', 'field1');
      expect(fieldMetric).toBeDefined();
      expect(fieldMetric?.interactionCount).toBe(2);
      expect(fieldMetric?.errorCount).toBe(1);
    });
  });

  describe('Export', () => {
    test('should export analytics as JSON', () => {
      analytics.trackFormStart('form1', 'user1');
      analytics.trackFormSubmit('form1', 'user1');

      const exported = analytics.exportAnalytics('form1');
      const parsed = JSON.parse(exported);

      expect(Array.isArray(parsed)).toBe(true);
      expect(parsed.length).toBeGreaterThan(0);
    });

    test('should export metrics as JSON', () => {
      analytics.trackFormStart('form1', 'user1');
      analytics.trackFormSubmit('form1', 'user1');

      const exported = analytics.exportMetrics('form1');
      const parsed = JSON.parse(exported);

      expect(parsed.formId).toBe('form1');
      expect(parsed.completionRate).toBe(100);
    });
  });

  describe('Event Callbacks', () => {
    test('should trigger callback on event', () => {
      const callback = jest.fn();
      analytics.onEventTracked(callback);

      analytics.trackFormStart('form1', 'user1');

      expect(callback).toHaveBeenCalled();
    });
  });

  describe('Performance', () => {
    test('should track 1000 events efficiently', () => {
      const start = performance.now();

      for (let i = 0; i < 1000; i++) {
        analytics.trackFieldInteraction('form1', `field${i % 10}`, `user${i % 5}`);
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(500);
    });

    test('should calculate metrics quickly', () => {
      for (let i = 0; i < 100; i++) {
        analytics.trackFormStart('form1', `user${i}`);
        if (i % 2 === 0) {
          analytics.trackFormSubmit('form1', `user${i}`);
        }
      }

      const start = performance.now();

      for (let i = 0; i < 100; i++) {
        analytics.getFormMetrics('form1');
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(200);
    });
  });

  describe('Storage', () => {
    test('should persist events to storage', () => {
      analytics.trackFormStart('form1', 'user1');
      analytics.trackFormSubmit('form1', 'user1');

      // Create new instance to test loading
      const newAnalytics = new FormAnalytics();
      const events = newAnalytics.getAllEvents('form1');

      expect(events.length).toBeGreaterThan(0);
    });
  });
});
