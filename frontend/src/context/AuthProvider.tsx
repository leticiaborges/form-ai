import { useState, type ReactNode } from 'react';
import type { AuthUser } from '../types/auth';
import { AuthContext } from './AuthContext';

export function AuthProvider({ children }: { children: ReactNode }) {
    const [user, setUser] = useState<AuthUser | null>(() => {
        const stored = localStorage.getItem('user');
        if (!stored) return null;
        try {
            return JSON.parse(stored);
        } catch {
            localStorage.removeItem('user');
            return null;
        }
    });

    function login(accessToken: string, refreshToken: string, user: AuthUser) {
        localStorage.setItem('accessToken', accessToken);
        localStorage.setItem('refreshToken', refreshToken);
        localStorage.setItem('user', JSON.stringify(user));
        setUser(user);
    }

    function logout() {
        localStorage.removeItem('accessToken');
        localStorage.removeItem('refreshToken');
        localStorage.removeItem('user');
        setUser(null);
        window.location.href = "/";
    }

    return (
        <AuthContext.Provider value={{ user, isAuthenticated: user !== null, login, logout }}>
            {children}
        </AuthContext.Provider>
    );
}
