"use client";

import Checkbox from "@mui/material/Checkbox";
import FormControl from "@mui/material/FormControl";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormGroup from "@mui/material/FormGroup";
import FormHelperText from "@mui/material/FormHelperText";
import FormLabel from "@mui/material/FormLabel";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import type { BranchDto } from "@/lib/api/identity";

type BranchScopeFieldProps = {
    allBranches: boolean;
    branchIds: string[];
    branches: BranchDto[];
    /** Çağıranın erişebildiği şube mi? Değilse seçilemez. */
    canAccessBranch: (id: string) => boolean;
    /** Çağıran tüm şubelere erişiyor mu? Değilse "Tüm şubeler" seçilemez. */
    canGrantAll: boolean;
    disabled?: boolean;
    error?: string;
    onChange(next: { allBranches: boolean; branchIds: string[] }): void;
};

/** Rolün şube kapsamı: tüm şubeler ya da seçili şubeler. */
export default function BranchScopeField({ allBranches, branchIds, branches, canAccessBranch, canGrantAll, disabled, error, onChange }: BranchScopeFieldProps) {
    const toggle = (id: string, checked: boolean) =>
        onChange({ allBranches, branchIds: checked ? [...branchIds, id] : branchIds.filter((b) => b !== id) });

    return (
        <FormControl error={!!error} disabled={disabled} component="fieldset">
            <FormLabel component="legend">Şube kapsamı</FormLabel>
            <RadioGroup
                value={allBranches ? "all" : "selected"}
                onChange={(e) => onChange({ allBranches: e.target.value === "all", branchIds })}
            >
                <FormControlLabel value="all" control={<Radio size="small" />} label="Tüm şubeler" disabled={!canGrantAll} />
                <FormControlLabel value="selected" control={<Radio size="small" />} label="Seçili şubeler" />
            </RadioGroup>
            {!allBranches && (
                <FormGroup sx={{ pl: 4 }}>
                    {branches.map((b) => (
                        <FormControlLabel
                            key={b.id}
                            label={b.name}
                            control={
                                <Checkbox
                                    size="small"
                                    checked={branchIds.includes(b.id)}
                                    disabled={disabled || !canAccessBranch(b.id)}
                                    onChange={(e) => toggle(b.id, e.target.checked)}
                                />
                            }
                        />
                    ))}
                </FormGroup>
            )}
            {error && <FormHelperText>{error}</FormHelperText>}
        </FormControl>
    );
}
