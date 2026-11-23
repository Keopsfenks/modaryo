import { permanentRedirect } from "next/navigation";

// Eski bağlantılar için: üyeler ekranı ayarlar merkezine taşındı.
export default function Page() {
    permanentRedirect("/ayarlar/kullanicilar");
}
