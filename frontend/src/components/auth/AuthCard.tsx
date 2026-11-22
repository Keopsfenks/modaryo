import Box from "@mui/material/Box";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";

type AuthCardProps = {
    title: string;
    subtitle?: React.ReactNode;
    children: React.ReactNode;
    footer?: React.ReactNode;
};

export default function AuthCard({ title, subtitle, children, footer }: AuthCardProps) {
    return (
        <Card>
            <CardContent>
                <Typography variant="h3" component="h1" sx={{ mb: subtitle ? 1 : 3 }}>
                    {title}
                </Typography>
                {subtitle && (
                    <Typography color="text.secondary" sx={{ mb: 3 }}>
                        {subtitle}
                    </Typography>
                )}
                {children}
                {footer && <Box sx={{ mt: 3, textAlign: "center" }}>{footer}</Box>}
            </CardContent>
        </Card>
    );
}
