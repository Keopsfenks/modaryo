import Box from "@mui/material/Box";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";

type DashboardCardProps = {
    title?: string;
    subtitle?: string;
    action?: React.ReactNode;
    footer?: React.ReactNode;
    children: React.ReactNode;
};

export default function DashboardCard({ title, subtitle, action, footer, children }: DashboardCardProps) {
    return (
        <Card sx={{ height: "100%", display: "flex", flexDirection: "column" }}>
            <CardContent sx={{ flexGrow: 1 }}>
                {(title || action) && (
                    <Box sx={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 2, mb: 3 }}>
                        <Box>
                            {title && (
                                <Typography variant="h5" component="h2">
                                    {title}
                                </Typography>
                            )}
                            {subtitle && (
                                <Typography variant="subtitle1" color="text.secondary">
                                    {subtitle}
                                </Typography>
                            )}
                        </Box>
                        {action}
                    </Box>
                )}
                {children}
            </CardContent>
            {footer}
        </Card>
    );
}
