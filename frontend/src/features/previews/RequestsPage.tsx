// "Requests and reports" is shown as pictures of the planned screen. There is no data behind it yet.
import actions from '../../assets/previews/requests-actions.png'
import table from '../../assets/previews/requests-table.png'
import { PreviewImage, PreviewNote } from '../../shared/ui/components'
import './previews.css'

export default function RequestsPage() {
  return (
    <>
      <div className="page-header">
        <h1>בקשות ודיווחים</h1>
        <PreviewImage plain src={actions} alt="כפתורים שאינם פעילים: בקשה חדשה, דיווח מחלה" width={343} height={40} />
      </div>
      <PreviewNote />
      <PreviewImage
        src={table}
        alt={
          'טבלת בקשות לדוגמה, 5 מתוך 40: חופשה שנתית של 5 ימים – אושר; החזר נסיעות עבודה – בטיפול; ' +
          'קורס פיתוח מקצועי – נדחה; היעדרות רפואית של יום אחד – אושר; מחשב נייד חדש – בטיפול'
        }
        width={1314}
        height={617}
      />
    </>
  )
}
