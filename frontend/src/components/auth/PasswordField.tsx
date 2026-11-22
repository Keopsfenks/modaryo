"use client";

import { useState } from "react";
import IconButton from "@mui/material/IconButton";
import InputAdornment from "@mui/material/InputAdornment";
import TextField, { type TextFieldProps } from "@mui/material/TextField";
import { IconEye, IconEyeOff } from "@tabler/icons-react";

/** Göster/gizle butonlu şifre alanı. */
export default function PasswordField(props: TextFieldProps) {
    const [visible, setVisible] = useState(false);

    return (
        <TextField
            {...props}
            type={visible ? "text" : "password"}
            slotProps={{
                ...props.slotProps,
                input: {
                    endAdornment: (
                        <InputAdornment position="end">
                            <IconButton
                                aria-label={visible ? "Şifreyi gizle" : "Şifreyi göster"}
                                onClick={() => setVisible((v) => !v)}
                                edge="end"
                                size="small"
                            >
                                {visible ? <IconEyeOff size={18} /> : <IconEye size={18} />}
                            </IconButton>
                        </InputAdornment>
                    ),
                },
            }}
        />
    );
}
