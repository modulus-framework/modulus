/**
 * FormAuthManager - Authentication and session management for forms
 * Handles user identity verification and secure form access
 */

export interface User {
  id: string;
  email: string;
  name: string;
  roles: string[];
  permissions: string[];
  lastLogin?: number;
  sessionId?: string;
}

export interface Credentials {
  email: string;
  password: string;
  rememberMe?: boolean;
}

export interface AuthSession {
  userId: string;
  sessionId: string;
  token: string;
  expiresAt: number;
  createdAt: number;
}

export interface AuthEvent {
  type: 'login' | 'logout' | 'sessionExpired' | 'authFailed';
  userId?: string;
  timestamp: number;
  details?: string;
}

export class FormAuthManager {
  private currentUser: User | null = null;
  private currentSession: AuthSession | null = null;
  private authEndpoint: string;
  private sessionTimeout: number = 3600000; // 1 hour
  private sessionCheckInterval: NodeJS.Timeout | null = null;
  private onAuthChange: ((user: User | null) => void) | null = null;
  private onSessionExpired: ((event: AuthEvent) => void) | null = null;
  private authHistory: AuthEvent[] = [];
  private maxHistorySize = 100;

  constructor(authEndpoint: string) {
    this.authEndpoint = authEndpoint;
    this.initializeSession();
  }

  /**
   * Authenticate user with credentials
   */
  async authenticate(credentials: Credentials): Promise<User | null> {
    try {
      const response = await fetch(`${this.authEndpoint}/authenticate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(credentials)
      });

      if (!response.ok) {
        this.recordAuthEvent({
          type: 'authFailed',
          timestamp: Date.now(),
          details: `Authentication failed: ${response.statusText}`
        });
        return null;
      }

      const data = await response.json();
      const user: User = data.user;
      const session: AuthSession = data.session;

      this.currentUser = user;
      this.currentSession = session;

      // Store session in localStorage
      if (credentials.rememberMe) {
        localStorage.setItem('auth_session', JSON.stringify(session));
      } else {
        sessionStorage.setItem('auth_session', JSON.stringify(session));
      }

      this.startSessionCheck();
      this.recordAuthEvent({
        type: 'login',
        userId: user.id,
        timestamp: Date.now()
      });

      if (this.onAuthChange) {
        this.onAuthChange(user);
      }

      return user;
    } catch (error) {
      console.error('Authentication error:', error);
      this.recordAuthEvent({
        type: 'authFailed',
        timestamp: Date.now(),
        details: String(error)
      });
      return null;
    }
  }

  /**
   * Logout current user
   */
  logout(): void {
    const userId = this.currentUser?.id;

    this.currentUser = null;
    this.currentSession = null;

    localStorage.removeItem('auth_session');
    sessionStorage.removeItem('auth_session');

    this.stopSessionCheck();

    this.recordAuthEvent({
      type: 'logout',
      userId,
      timestamp: Date.now()
    });

    if (this.onAuthChange) {
      this.onAuthChange(null);
    }
  }

  /**
   * Check if user is authenticated
   */
  isAuthenticated(): boolean {
    return this.currentUser !== null && this.currentSession !== null;
  }

  /**
   * Get current user
   */
  getCurrentUser(): User | null {
    return this.currentUser;
  }

  /**
   * Get current session
   */
  getCurrentSession(): AuthSession | null {
    return this.currentSession;
  }

  /**
   * Check if user has permission
   */
  hasPermission(permission: string): boolean {
    if (!this.currentUser) return false;
    return this.currentUser.permissions.includes(permission);
  }

  /**
   * Check if user has role
   */
  hasRole(role: string): boolean {
    if (!this.currentUser) return false;
    return this.currentUser.roles.includes(role);
  }

  /**
   * Check if user can access form
   */
  async canAccessForm(formId: string): Promise<boolean> {
    if (!this.isAuthenticated()) {
      return false;
    }

    try {
      const response = await fetch(`${this.authEndpoint}/authorize/form/${formId}`, {
        headers: {
          'Authorization': `Bearer ${this.currentSession?.token}`
        }
      });

      return response.ok;
    } catch (error) {
      console.error('Authorization check failed:', error);
      return false;
    }
  }

  /**
   * Check if user can access field
   */
  async canAccessField(formId: string, fieldId: string): Promise<boolean> {
    if (!this.isAuthenticated()) {
      return false;
    }

    try {
      const response = await fetch(
        `${this.authEndpoint}/authorize/form/${formId}/field/${fieldId}`,
        {
          headers: {
            'Authorization': `Bearer ${this.currentSession?.token}`
          }
        }
      );

      return response.ok;
    } catch (error) {
      console.error('Field authorization check failed:', error);
      return false;
    }
  }

  /**
   * Get auth token
   */
  getAuthToken(): string | null {
    return this.currentSession?.token || null;
  }

  /**
   * Check if session is valid
   */
  isSessionValid(): boolean {
    if (!this.currentSession) return false;
    return Date.now() < this.currentSession.expiresAt;
  }

  /**
   * Refresh session
   */
  async refreshSession(): Promise<boolean> {
    if (!this.currentSession) {
      return false;
    }

    try {
      const response = await fetch(`${this.authEndpoint}/refresh`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${this.currentSession.token}`
        }
      });

      if (!response.ok) {
        this.logout();
        return false;
      }

      const data = await response.json();
      this.currentSession = data.session;

      // Update stored session
      const storage = localStorage.getItem('auth_session') ? localStorage : sessionStorage;
      storage.setItem('auth_session', JSON.stringify(this.currentSession));

      return true;
    } catch (error) {
      console.error('Session refresh failed:', error);
      this.logout();
      return false;
    }
  }

  /**
   * Set auth change callback
   */
  onAuthChanged(callback: (user: User | null) => void): FormAuthManager {
    this.onAuthChange = callback;
    return this;
  }

  /**
   * Set session expired callback
   */
  onSessionExpiredCallback(callback: (event: AuthEvent) => void): FormAuthManager {
    this.onSessionExpired = callback;
    return this;
  }

  /**
   * Get auth history
   */
  getAuthHistory(): AuthEvent[] {
    return [...this.authHistory];
  }

  /**
   * Export auth state
   */
  exportAuthState(): {
    user: User | null;
    isAuthenticated: boolean;
  } {
    return {
      user: this.currentUser,
      isAuthenticated: this.isAuthenticated()
    };
  }

  /**
   * Record auth event
   */
  private recordAuthEvent(event: AuthEvent): void {
    this.authHistory.push(event);

    if (this.authHistory.length > this.maxHistorySize) {
      this.authHistory.shift();
    }
  }

  /**
   * Initialize session from storage
   */
  private initializeSession(): void {
    // Try to restore from localStorage or sessionStorage
    let sessionData = localStorage.getItem('auth_session') ||
                     sessionStorage.getItem('auth_session');

    if (sessionData) {
      try {
        const session: AuthSession = JSON.parse(sessionData);

        // Check if session is still valid
        if (Date.now() < session.expiresAt) {
          this.currentSession = session;
          this.startSessionCheck();
        } else {
          localStorage.removeItem('auth_session');
          sessionStorage.removeItem('auth_session');
        }
      } catch (error) {
        console.error('Failed to restore session:', error);
      }
    }
  }

  /**
   * Start session validity check
   */
  private startSessionCheck(): void {
    if (this.sessionCheckInterval) {
      clearInterval(this.sessionCheckInterval);
    }

    // Check every 5 minutes
    this.sessionCheckInterval = setInterval(() => {
      if (!this.isSessionValid()) {
        this.recordAuthEvent({
          type: 'sessionExpired',
          userId: this.currentUser?.id,
          timestamp: Date.now()
        });

        if (this.onSessionExpired) {
          this.onSessionExpired({
            type: 'sessionExpired',
            userId: this.currentUser?.id,
            timestamp: Date.now()
          });
        }

        this.logout();
      }
    }, 300000);
  }

  /**
   * Stop session check
   */
  private stopSessionCheck(): void {
    if (this.sessionCheckInterval) {
      clearInterval(this.sessionCheckInterval);
      this.sessionCheckInterval = null;
    }
  }

  /**
   * Cleanup
   */
  destroy(): void {
    this.stopSessionCheck();
    this.logout();
  }
}
