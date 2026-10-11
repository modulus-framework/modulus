/**
 * FormStateManagerTests - Tests for form state management
 * Covers state storage, persistence, and recovery across steps
 */

import { FormStateManager, FormData } from '../wwwroot/js/form-state-manager';

describe('FormStateManager - Memory Storage', () => {
    let stateManager: FormStateManager;

    beforeEach(() => {
        stateManager = new FormStateManager('memory');
    });

    describe('Basic State Operations', () => {
        test('should set and get individual state values', () => {
            stateManager.setState('username', 'john_doe');
            expect(stateManager.getState('username')).toBe('john_doe');
        });

        test('should set and get multiple values', () => {
            stateManager.setState('firstName', 'John');
            stateManager.setState('lastName', 'Doe');
            stateManager.setState('email', 'john@example.com');

            expect(stateManager.getState('firstName')).toBe('John');
            expect(stateManager.getState('lastName')).toBe('Doe');
            expect(stateManager.getState('email')).toBe('john@example.com');
        });

        test('should get all state as object', () => {
            stateManager.setState('name', 'John');
            stateManager.setState('email', 'john@example.com');

            const allState = stateManager.getAllState();
            expect(allState.name).toBe('John');
            expect(allState.email).toBe('john@example.com');
        });

        test('should set multiple state at once', () => {
            const data = {
                firstName: 'John',
                lastName: 'Doe',
                email: 'john@example.com'
            };

            stateManager.setMultipleState(data);

            expect(stateManager.getState('firstName')).toBe('John');
            expect(stateManager.getState('lastName')).toBe('Doe');
            expect(stateManager.getState('email')).toBe('john@example.com');
        });

        test('should merge state data', () => {
            stateManager.setState('firstName', 'John');
            stateManager.mergeState({ lastName: 'Doe', email: 'john@example.com' });

            expect(stateManager.getState('firstName')).toBe('John');
            expect(stateManager.getState('lastName')).toBe('Doe');
        });

        test('should check if state key exists', () => {
            stateManager.setState('username', 'john');

            expect(stateManager.hasState('username')).toBe(true);
            expect(stateManager.hasState('password')).toBe(false);
        });

        test('should return default value for missing key', () => {
            const value = stateManager.getStateWithDefault('missing', 'default');
            expect(value).toBe('default');
        });

        test('should clear specific state key', () => {
            stateManager.setState('firstName', 'John');
            stateManager.setState('lastName', 'Doe');

            stateManager.clearStateKey('firstName');

            expect(stateManager.hasState('firstName')).toBe(false);
            expect(stateManager.hasState('lastName')).toBe(true);
        });

        test('should clear all state', () => {
            stateManager.setState('firstName', 'John');
            stateManager.setState('lastName', 'Doe');
            stateManager.setState('email', 'john@example.com');

            stateManager.clearState();

            expect(stateManager.getAllState()).toEqual({});
        });
    });

    describe('State Change Callbacks', () => {
        test('should trigger callback on state change', (done) => {
            let callbackFired = false;
            let changedState: FormData | null = null;

            stateManager.onStateChangeCallback((state) => {
                callbackFired = true;
                changedState = state;
            });

            stateManager.setState('username', 'john');

            setTimeout(() => {
                expect(callbackFired).toBe(true);
                expect(changedState?.username).toBe('john');
                done();
            }, 10);
        });

        test('should not trigger callback when value unchanged', (done) => {
            let callCount = 0;

            stateManager.setState('username', 'john');

            stateManager.onStateChangeCallback(() => {
                callCount++;
            });

            stateManager.setState('username', 'john'); // Same value
            stateManager.setState('username', 'jane'); // Different value

            setTimeout(() => {
                expect(callCount).toBe(1); // Only fired once
                done();
            }, 10);
        });
    });

    describe('Dirty State Tracking', () => {
        test('should track if state is dirty', () => {
            expect(stateManager.isDirtyState()).toBe(false);

            stateManager.setState('username', 'john');
            expect(stateManager.isDirtyState()).toBe(true);
        });

        test('should mark state as clean', () => {
            stateManager.setState('username', 'john');
            expect(stateManager.isDirtyState()).toBe(true);

            stateManager.markClean();
            expect(stateManager.isDirtyState()).toBe(false);
        });
    });

    describe('Serialization', () => {
        test('should export state as JSON', () => {
            stateManager.setState('firstName', 'John');
            stateManager.setState('lastName', 'Doe');

            const json = stateManager.toJSON();
            const parsed = JSON.parse(json);

            expect(parsed.firstName).toBe('John');
            expect(parsed.lastName).toBe('Doe');
        });

        test('should import state from JSON', () => {
            const json = '{"firstName":"John","lastName":"Doe"}';

            expect(stateManager.fromJSON(json)).toBe(true);
            expect(stateManager.getState('firstName')).toBe('John');
            expect(stateManager.getState('lastName')).toBe('Doe');
        });

        test('should handle invalid JSON gracefully', () => {
            const result = stateManager.fromJSON('invalid json');
            expect(result).toBe(false);
        });
    });

    describe('History/Undo', () => {
        test('should save state to history', () => {
            stateManager.setState('value', 1);
            stateManager.saveToHistory();

            stateManager.setState('value', 2);

            expect(stateManager.canUndo()).toBe(true);
        });

        test('should undo to previous state', () => {
            stateManager.setState('value', 1);
            stateManager.saveToHistory();

            stateManager.setState('value', 2);
            stateManager.setState('value', 3);

            stateManager.undo();
            expect(stateManager.getState('value')).toBe(2);
        });

        test('should return false when no history available', () => {
            const result = stateManager.undo();
            expect(result).toBe(false);
        });

        test('should get history count', () => {
            stateManager.saveToHistory();
            stateManager.saveToHistory();
            stateManager.saveToHistory();

            expect(stateManager.getHistoryCount()).toBe(3);
        });

        test('should clear history', () => {
            stateManager.saveToHistory();
            stateManager.saveToHistory();

            stateManager.clearHistory();

            expect(stateManager.getHistoryCount()).toBe(0);
            expect(stateManager.canUndo()).toBe(false);
        });

        test('should limit history size', () => {
            const manager = new FormStateManager('memory');

            for (let i = 0; i < 60; i++) {
                manager.setState('value', i);
                manager.saveToHistory();
            }

            // Should only keep last 50 entries
            expect(manager.getHistoryCount()).toBeLessThanOrEqual(50);
        });
    });

    describe('Validation', () => {
        test('should validate required keys exist', () => {
            stateManager.setState('firstName', 'John');
            stateManager.setState('lastName', 'Doe');

            const result = stateManager.validateStructure(['firstName', 'lastName']);
            expect(result.valid).toBe(true);
            expect(result.missing.length).toBe(0);
        });

        test('should identify missing required keys', () => {
            stateManager.setState('firstName', 'John');

            const result = stateManager.validateStructure(['firstName', 'lastName', 'email']);
            expect(result.valid).toBe(false);
            expect(result.missing).toContain('lastName');
            expect(result.missing).toContain('email');
        });
    });

    describe('Utility Methods', () => {
        test('should calculate state size', () => {
            stateManager.setState('firstName', 'John');
            stateManager.setState('lastName', 'Doe');

            const size = stateManager.getStateSize();
            expect(size).toBeGreaterThan(0);
        });

        test('should clone state manager', () => {
            stateManager.setState('firstName', 'John');
            stateManager.setState('lastName', 'Doe');

            const clone = stateManager.clone();

            expect(clone.getState('firstName')).toBe('John');
            expect(clone.getState('lastName')).toBe('Doe');

            clone.setState('firstName', 'Jane');
            expect(stateManager.getState('firstName')).toBe('John');
        });
    });

    describe('CSV Import/Export', () => {
        test('should export state as CSV', () => {
            stateManager.setState('firstName', 'John');
            stateManager.setState('lastName', 'Doe');

            const csv = stateManager.exportAsCSV();
            expect(csv).toContain('firstName');
            expect(csv).toContain('John');
        });

        test('should import state from CSV', () => {
            const csv = 'firstName,lastName\nJohn,Doe';

            const result = stateManager.importFromCSV(csv);
            expect(result).toBe(true);
            expect(stateManager.getState('firstName')).toBe('John');
            expect(stateManager.getState('lastName')).toBe('Doe');
        });

        test('should handle invalid CSV gracefully', () => {
            const result = stateManager.importFromCSV('invalid');
            expect(result).toBe(false);
        });
    });
});

describe('FormStateManager - LocalStorage Persistence', () => {
    let stateManager: FormStateManager;

    beforeEach(() => {
        localStorage.clear();
        stateManager = new FormStateManager('localStorage', 'test-form-state');
    });

    afterEach(() => {
        localStorage.clear();
    });

    test('should persist state to localStorage', () => {
        stateManager.setState('username', 'john');
        stateManager.setState('email', 'john@example.com');

        const result = stateManager.persistToStorage();
        expect(result).toBe(true);

        const stored = localStorage.getItem('test-form-state');
        expect(stored).toContain('john');
    });

    test('should load state from localStorage', () => {
        localStorage.setItem('test-form-state', '{"username":"john","email":"john@example.com"}');

        const result = stateManager.loadFromStorage();
        expect(result).toBe(true);

        expect(stateManager.getState('username')).toBe('john');
        expect(stateManager.getState('email')).toBe('john@example.com');
    });

    test('should return false when loading non-existent storage', () => {
        const result = stateManager.loadFromStorage();
        expect(result).toBe(false);
    });

    test('should clear state from storage', () => {
        stateManager.setState('username', 'john');
        stateManager.persistToStorage();

        stateManager.clearFromStorage();

        const stored = localStorage.getItem('test-form-state');
        expect(stored).toBeNull();
    });

    test('should mark clean after persistence', () => {
        stateManager.setState('username', 'john');
        expect(stateManager.isDirtyState()).toBe(true);

        stateManager.persistToStorage();
        expect(stateManager.isDirtyState()).toBe(false);
    });
});

describe('FormStateManager - SessionStorage Persistence', () => {
    let stateManager: FormStateManager;

    beforeEach(() => {
        sessionStorage.clear();
        stateManager = new FormStateManager('sessionStorage', 'test-session-state');
    });

    afterEach(() => {
        sessionStorage.clear();
    });

    test('should persist state to sessionStorage', () => {
        stateManager.setState('tempData', 'value');

        const result = stateManager.persistToStorage();
        expect(result).toBe(true);

        const stored = sessionStorage.getItem('test-session-state');
        expect(stored).toContain('tempData');
    });

    test('should load state from sessionStorage', () => {
        sessionStorage.setItem('test-session-state', '{"tempData":"value"}');

        const result = stateManager.loadFromStorage();
        expect(result).toBe(true);

        expect(stateManager.getState('tempData')).toBe('value');
    });
});

describe('FormStateManager - Form Synchronization', () => {
    let stateManager: FormStateManager;
    let formElement: HTMLFormElement;

    beforeEach(() => {
        formElement = document.createElement('form');
        document.body.appendChild(formElement);

        const input1 = document.createElement('input');
        input1.type = 'text';
        input1.name = 'firstName';
        input1.value = '';
        formElement.appendChild(input1);

        const input2 = document.createElement('input');
        input2.type = 'email';
        input2.name = 'email';
        input2.value = '';
        formElement.appendChild(input2);

        const checkbox = document.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.name = 'subscribe';
        checkbox.checked = false;
        formElement.appendChild(checkbox);

        stateManager = new FormStateManager('memory');
    });

    afterEach(() => {
        document.body.removeChild(formElement);
    });

    test('should sync form values to state', () => {
        const input = formElement.querySelector('input[name="firstName"]') as HTMLInputElement;
        const emailInput = formElement.querySelector('input[name="email"]') as HTMLInputElement;

        input.value = 'John';
        emailInput.value = 'john@example.com';

        stateManager.syncWithForm(formElement);

        expect(stateManager.getState('firstName')).toBe('John');
        expect(stateManager.getState('email')).toBe('john@example.com');
    });

    test('should apply state to form fields', () => {
        stateManager.setState('firstName', 'John');
        stateManager.setState('email', 'john@example.com');

        stateManager.applyToForm(formElement);

        const input = formElement.querySelector('input[name="firstName"]') as HTMLInputElement;
        const emailInput = formElement.querySelector('input[name="email"]') as HTMLInputElement;

        expect(input.value).toBe('John');
        expect(emailInput.value).toBe('john@example.com');
    });

    test('should handle checkbox values correctly', () => {
        stateManager.setState('subscribe', true);

        stateManager.applyToForm(formElement);

        const checkbox = formElement.querySelector('input[name="subscribe"]') as HTMLInputElement;
        expect(checkbox.checked).toBe(true);
    });
});

describe('FormStateManager - Performance', () => {
    test('should handle large state efficiently', () => {
        const stateManager = new FormStateManager('memory');

        const start = performance.now();

        for (let i = 0; i < 1000; i++) {
            stateManager.setState(`field${i}`, `value${i}`);
        }

        const elapsed = performance.now() - start;

        expect(elapsed).toBeLessThan(100); // Should complete in under 100ms
        expect(stateManager.getAllState()).toHaveProperty('field0');
        expect(stateManager.getAllState()).toHaveProperty('field999');
    });

    test('should persist large state quickly', () => {
        const stateManager = new FormStateManager('localStorage', 'perf-test');

        for (let i = 0; i < 100; i++) {
            stateManager.setState(`field${i}`, `value${i}`);
        }

        const start = performance.now();
        stateManager.persistToStorage();
        const elapsed = performance.now() - start;

        expect(elapsed).toBeLessThan(50); // Should complete quickly

        localStorage.clear();
    });

    test('should load large state quickly', () => {
        localStorage.setItem('perf-test', JSON.stringify(
            Object.fromEntries(Array.from({ length: 100 }, (_, i) => [`field${i}`, `value${i}`]))
        ));

        const stateManager = new FormStateManager('localStorage', 'perf-test');

        const start = performance.now();
        stateManager.loadFromStorage();
        const elapsed = performance.now() - start;

        expect(elapsed).toBeLessThan(50); // Should complete quickly

        localStorage.clear();
    });
});
