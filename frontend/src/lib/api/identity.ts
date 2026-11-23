import { apiFetch } from "./client";

// Kimlik servisi (services/identity) uç noktalarının tipli sarmalayıcıları.
// Yollar Next.js rewrites ile http://localhost:5100/api/identity/... adresine gider.

export type CompanyRef = { id: string; name: string; roleName: string };

export type SessionInfo = {
    user: { id: string; email: string; fullName: string };
    activeCompany: CompanyRef | null;
    permissions: string[];
    companies: CompanyRef[];
    modules: string[];
    /** null → tüm şubeler; boş dizi → hiçbir şube. */
    branchIds: string[] | null;
    maxDiscountPercent: number;
    userType: "employee" | "dealer" | "self_service";
};

export type SessionListItem = {
    id: string;
    createdAt: string;
    ip: string | null;
    userAgent: string | null;
    isCurrent: boolean;
    rememberMe: boolean;
};

export type RoleDto = { id: string; name: string; systemKey: string | null };

export type MemberDto = {
    userId: string;
    email: string;
    fullName: string;
    roleId: string;
    roleName: string;
    roleSystemKey: string | null;
    joinedAt: string;
};

export type InvitationDto = { id: string; email: string; roleId: string; roleName: string; expiresAt: string; invitedBy: string };

export type MyInvitationDto = { id: string; companyName: string; invitedBy: string; roleName: string; expiresAt: string };

export type InvitationPreview = { companyName: string; invitedBy: string; email: string; expiresAt: string; accountExists: boolean };

export type CompanyDto = { id: string; name: string; createdAt: string };

const base = "/api/identity";
const post = <T = void>(path: string, body?: unknown) => apiFetch<T>(`${base}${path}`, { method: "POST", body });
const patch = (path: string, body: unknown) => apiFetch<void>(`${base}${path}`, { method: "PATCH", body });
const del = (path: string) => apiFetch<void>(`${base}${path}`, { method: "DELETE" });
const get = <T>(path: string) => apiFetch<T>(`${base}${path}`);

export const identity = {
    /** 202 → undefined (e-posta doğrulaması gerekir); davet linkiyle kayıtta 200 → oturum açılmıştır. */
    register: (body: { email: string; password: string; fullName: string; inviteToken?: string }) =>
        post<SessionInfo | undefined>("/auth/register", body),
    confirmEmail: (body: { userId: string; token: string }) => post<SessionInfo>("/auth/confirm-email", body),
    resendConfirmation: (email: string) => post("/auth/resend-confirmation", { email }),
    login: (body: { email: string; password: string; rememberMe: boolean }) => post<SessionInfo>("/auth/login", body),
    logout: () => post("/auth/logout"),
    logoutAll: () => post("/auth/logout-all"),
    getSession: () => get<SessionInfo>("/auth/session"),
    switchTenant: (tenantId: string) => post<SessionInfo>("/auth/session/tenant", { tenantId }),
    forgotPassword: (email: string) => post("/auth/forgot-password", { email }),
    resetPassword: (body: { email: string; token: string; newPassword: string }) => post("/auth/reset-password", body),

    changePassword: (body: { currentPassword: string; newPassword: string }) => post("/account/change-password", body),
    updateProfile: (fullName: string) => patch("/account/profile", { fullName }),
    getSessions: () => get<SessionListItem[]>("/account/sessions"),
    revokeSession: (id: string) => del(`/account/sessions/${encodeURIComponent(id)}`),
    getMyInvitations: () => get<MyInvitationDto[]>("/account/invitations"),
    acceptMyInvitation: (id: string) => post<SessionInfo>(`/account/invitations/${id}/accept`),

    getInvitationPreview: (token: string) => get<InvitationPreview>(`/invitations/${encodeURIComponent(token)}`),
    acceptInvitation: (token: string) => post<SessionInfo>(`/invitations/${encodeURIComponent(token)}/accept`),

    createCompany: (name: string) => post<CompanyDto>("/companies", { name }),
    getCurrentCompany: () => get<CompanyDto>("/companies/current"),
    updateCompany: (name: string) => patch("/companies/current", { name }),
    leaveCompany: () => post("/companies/current/leave"),
    getRoles: () => get<RoleDto[]>("/companies/current/roles"),
    getMembers: () => get<MemberDto[]>("/companies/current/members"),
    changeMemberRole: (userId: string, roleId: string) => patch(`/companies/current/members/${userId}`, { roleId }),
    removeMember: (userId: string) => del(`/companies/current/members/${userId}`),
    listInvitations: () => get<InvitationDto[]>("/companies/current/invitations"),
    createInvitation: (email: string, roleId: string) => post<InvitationDto>("/companies/current/invitations", { email, roleId }),
    resendInvitation: (id: string) => post(`/companies/current/invitations/${id}/resend`),
    revokeInvitation: (id: string) => del(`/companies/current/invitations/${id}`),
};
