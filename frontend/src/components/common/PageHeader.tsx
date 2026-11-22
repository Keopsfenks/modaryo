import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";

type PageHeaderProps = {
    title: string;
    subtitle?: string;
};

export default function PageHeader({ title, subtitle }: PageHeaderProps) {
    return (
        <Box sx={{ mb: 3 }}>
            <Typography variant="h3" component="h1" color="text.primary">
                {title}
            </Typography>
            {subtitle && (
                <Typography color="text.secondary" sx={{ mt: 0.5 }}>
                    {subtitle}
                </Typography>
            )}
        </Box>
    );
}
