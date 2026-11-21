/**
 * Sample events for the Events tab. There is no events data in the system yet, so every day
 * shows this same list; it is long enough to show that the list scrolls.
 */
interface SampleEvent {
  time: string
  title: string
  description: string
  kind: string
}

export const SAMPLE_EVENTS: SampleEvent[] = [
  { time: '09:00', title: 'פתיחת יום - עדכון צוות', description: 'עדכון קצר על משימות היום', kind: 'ישיבה' },
  { time: '10:00', title: 'צוות פיתוח - ישיבה שבועית', description: 'סקירת התקדמות פרויקטים', kind: 'ישיבה' },
  { time: '11:30', title: 'שיחת לקוח', description: 'הצגת גרסה חדשה ללקוח', kind: 'פגישה' },
  { time: '13:00', title: 'ארוחת צהריים מחלקתית', description: 'חדר האוכל, קומה 2', kind: 'חברה' },
  { time: '14:00', title: 'הכשרת בטיחות', description: 'הכשרה חובה לכל העובדים', kind: 'הכשרה' },
  { time: '15:30', title: 'סדנת כלים חדשים', description: 'היכרות עם מערכת הדיווח החדשה', kind: 'הכשרה' },
  { time: '16:15', title: 'שיחה אישית עם ראש הצוות', description: 'מעקב יעדים רבעוני', kind: 'פגישה' },
  { time: '17:00', title: 'סיכום שבועי', description: 'סגירת משימות ותכנון השבוע הבא', kind: 'ישיבה' },
]
