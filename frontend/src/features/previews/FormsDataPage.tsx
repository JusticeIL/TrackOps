// "Forms and data" is shown as pictures of the planned screen. There is no data behind it yet;
// each picture's alt text says what the picture shows.
import monthHours from '../../assets/previews/data-month-hours.png'
import sickDays from '../../assets/previews/data-sick-days.png'
import vacationDays from '../../assets/previews/data-vacation-days.png'
import weekChart from '../../assets/previews/data-week-chart.png'
import weeklyAverage from '../../assets/previews/data-weekly-average.png'
import { PreviewImage } from '../../shared/ui/components'
import './previews.css'

export default function FormsDataPage() {
  return (
    <>
      <div className="page-header">
        <h1>טפסים ונתונים</h1>
      </div>
      <div className="preview-columns">
        <section className="preview-group" aria-label="שעות עבודה">
          <PreviewImage src={monthHours} alt="סה״כ שעות החודש: 157.5, עלייה של 12.5% מהחודש הקודם" width={280} height={170} />
          <PreviewImage src={weeklyAverage} alt="ממוצע שעות שבועי: 35.7, ירידה של 7% מהשבוע הקודם" width={280} height={170} />
          <PreviewImage
            src={weekChart}
            alt="שעות עבודה השבוע: ראשון 8.5, שני 7.2, שלישי 8, רביעי 6.8, חמישי 9.1, שישי 4.5, שבת 0"
            width={583}
            height={404}
          />
        </section>
        <section className="preview-group" aria-label="ימי חופשה ומחלה">
          <PreviewImage src={vacationDays} alt="ימי חופשה: נוצלו 10 מתוך 18, נשארו 8 עד סוף השנה" width={280} height={200} />
          <PreviewImage src={sickDays} alt="ימי מחלה: נוצלו 6 מתוך 10, נשארו 4 עד סוף השנה" width={280} height={200} />
        </section>
      </div>
    </>
  )
}
