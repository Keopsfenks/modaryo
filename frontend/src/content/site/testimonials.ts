export type Testimonial = { quote: string; name: string; role: string; company: string };

/** Gerçek müşteri yorumu yokken boş kalır; bileşen boşken hiçbir şey çizmez. */
export const testimonials: Testimonial[] = [];
