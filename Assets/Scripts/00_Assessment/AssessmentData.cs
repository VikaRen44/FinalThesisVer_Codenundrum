using System;
using System.Collections.Generic;

[Serializable]
public class AssessmentQuestion
{
    public string question;
    public List<string> choices;
    public int correctIndex;
}

[Serializable]
public class AssessmentQuestionList
{
    public List<AssessmentQuestion> questions;
}
