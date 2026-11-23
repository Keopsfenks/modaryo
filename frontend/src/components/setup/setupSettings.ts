import { erp } from "@/lib/api/erp";

export type SetupSettings = { completedAt: string | null; dismissedAt: string | null; sector: string | null };

/** kurulum kategorisini okur, yama uygular ve sürümüyle kaydeder (diğer alanlar korunur). */
export async function patchSetup(patch: Partial<SetupSettings>): Promise<SetupSettings> {
    const current = await erp.settings.get<SetupSettings>("kurulum");
    const dto = await erp.settings.put<SetupSettings>("kurulum", current.version, { ...current.value, ...patch });
    return dto.value;
}
