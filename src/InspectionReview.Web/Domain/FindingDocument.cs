namespace InspectionReview.Web.Domain;

public class FindingDocument
{
    public int FindingId { get; set; }
    public Finding? Finding { get; set; }

    public int DocumentId { get; set; }
    public Document? Document { get; set; }
}
