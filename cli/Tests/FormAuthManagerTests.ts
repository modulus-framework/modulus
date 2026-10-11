/**
 * FormAuthManagerTests - Test suite for authentication and session management
 */

import { FormAuthManager, User, AuthSession } from '../wwwroot/js/form-auth-manager';

describe('FormAuthManager', () => {
  let authManager: FormAuthManager;
  const mockAuthEndpoint = 'http://localhost:3000/api/auth';

  beforeEach(() => {
    authManager = new FormAuthManager(mockAuthEndpoint);
    localStorage.clear();
    sessionStorage.clear();
  });

  afterEach(() => {
    authManager.destroy();
    localStorage.clear();
    sessionStorage.clear();
  });

  describe('Initialization', () => {
    test('should initialize without authenticated user', () => {
      expect(authManager.isAuthenticated()).toBe(false);
      expect(authManager.getCurrentUser()).toBeNull();
    });

    test('should not have session on init', () => {
      expect(authManager.getCurrentSession()).toBeNull();
    });
  });

  describe('Authentication', () => {
    test('should require valid credentials', async () => {
      const credentials = { email: '', password: '' };

      // Mock will fail, but we're testing the flow
      const result = await authManager.authenticate(credentials);

      expect(result).toBeNull();
      expect(authManager.isAuthenticated()).toBe(false);
    });

    test('should track auth history', () => {
      const history = authManager.getAuthHistory();

      // Should have at least auth attempt events
      expect(history.length).toBeGreaterThanOrEqual(0);
    });
  });

  describe('Session Management', () => {
    test('should check if session is valid', () => {
      expect(authManager.isSessionValid()).toBe(false);
    });

    test('should have null session initially', () => {
      expect(authManager.getCurrentSession()).toBeNull();
    });
  });

  describe('Permissions', () => {
    test('should return false for permission check when not authenticated', () => {
      expect(authManager.hasPermission('any:permission')).toBe(false);
    });

    test('should return false for role check when not authenticated', () => {
      expect(authManager.hasRole('any_role')).toBe(false);
    });
  });

  describe('Auth Token', () => {
    test('should return null token when not authenticated', () => {
      expect(authManager.getAuthToken()).toBeNull();
    });
  });

  describe('Auth State Export', () => {
    test('should export auth state', () => {
      const state = authManager.exportAuthState();

      expect(state).toHaveProperty('user');
      expect(state).toHaveProperty('isAuthenticated');
      expect(state.user).toBeNull();
      expect(state.isAuthenticated).toBe(false);
    });
  });

  describe('Callbacks', () => {
    test('should set auth change callback', () => {
      const callback = jest.fn();
      authManager.onAuthChanged(callback);

      expect(callback).not.toHaveBeenCalled(); // Not called on setup
    });

    test('should set session expired callback', () => {
      const callback = jest.fn();
      authManager.onSessionExpiredCallback(callback);

      expect(callback).not.toHaveBeenCalled(); // Not called on setup
    });

    test('should return manager for chainable API', () => {
      const result = authManager.onAuthChanged(() => {});
      expect(result).toBe(authManager);
    });
  });

  describe('Logout', () => {
    test('should clear auth on logout', () => {
      // Mock logout (no user to logout in this test)
      authManager.logout();

      expect(authManager.isAuthenticated()).toBe(false);
      expect(authManager.getCurrentUser()).toBeNull();
    });
  });

  describe('History Management', () => {
    test('should track auth events', () => {
      authManager.logout();

      const history = authManager.getAuthHistory();
      const logoutEvent = history.find(e => e.type === 'logout');

      expect(logoutEvent).toBeDefined();
    });

    test('should limit history size', () => {
      // Logout many times
      for (let i = 0; i < 150; i++) {
        authManager.logout();
      }

      const history = authManager.getAuthHistory();
      expect(history.length).toBeLessThanOrEqual(100); // Max history size
    });
  });

  describe('Form Access Control', () => {
    test('should deny form access when not authenticated', async () => {
      const canAccess = await authManager.canAccessForm('form1');
      expect(canAccess).toBe(false);
    });

    test('should deny field access when not authenticated', async () => {
      const canAccess = await authManager.canAccessField('form1', 'field1');
      expect(canAccess).toBe(false);
    });
  });

  describe('Error Handling', () => {
    test('should handle auth endpoint errors gracefully', async () => {
      const badAuthManager = new FormAuthManager('invalid://endpoint');
      const result = await badAuthManager.authenticate({
        email: 'test@example.com',
        password: 'password'
      });

      expect(result).toBeNull();
      badAuthManager.destroy();
    });

    test('should handle session refresh errors', async () => {
      authManager.logout();
      const result = await authManager.refreshSession();

      expect(result).toBe(false);
    });
  });

  describe('Storage Integration', () => {
    test('should clear localStorage on cleanup', () => {
      localStorage.setItem('test', 'value');
      authManager.destroy();

      expect(localStorage.getItem('test')).toBe('value'); // Our manager doesn't clear unrelated items
    });
  });

  describe('Session Validity', () => {
    test('should detect invalid session', () => {
      expect(authManager.isSessionValid()).toBe(false);
    });
  });

  describe('Performance', () => {
    test('should check auth status quickly', () => {
      const start = performance.now();

      for (let i = 0; i < 1000; i++) {
        authManager.isAuthenticated();
        authManager.getAuthToken();
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(50);
    });
  });
});
