BEGIN;

CREATE OR REPLACE FUNCTION pg_temp.reqara_seed_uuid(value text)
RETURNS uuid
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT (
        substr(md5(value), 1, 8) || '-' ||
        substr(md5(value), 9, 4) || '-' ||
        '5' || substr(md5(value), 14, 3) || '-' ||
        'a' || substr(md5(value), 18, 3) || '-' ||
        substr(md5(value), 21, 12)
    )::uuid;
$$;

CREATE TEMP TABLE reqara_seed_settings (
    category text NOT NULL,
    key text NOT NULL,
    value_json jsonb NOT NULL,
    is_secret boolean NOT NULL DEFAULT FALSE,
    PRIMARY KEY (category, key)
) ON COMMIT DROP;

INSERT INTO reqara_seed_settings (category, key, value_json) VALUES
('email', 'provider', '"resend"'),
('Email:Resend', 'From', '"requests@reqara.com"'),
('Email:Resend', 'FromName', '"Reqara"'),
('Email', 'ReplyTo', '"support@reqara.com"'),
('app', 'baseUrl', '"https://reqara.com"');

INSERT INTO admin_settings (id, organization_id, scope, category, key, value_json, is_secret, created_at, updated_at, updated_by_user_id)
SELECT
    pg_temp.reqara_seed_uuid('admin-setting:' || lower(category) || ':' || lower(key)),
    NULL,
    1,
    category,
    key,
    value_json,
    is_secret,
    now(),
    now(),
    NULL
FROM reqara_seed_settings s
WHERE NOT EXISTS (
    SELECT 1
    FROM admin_settings existing
    WHERE existing.organization_id IS NULL
      AND existing.category = s.category
      AND existing.key = s.key
);

UPDATE admin_settings existing
SET value_json = s.value_json,
    is_secret = s.is_secret,
    updated_at = now()
FROM reqara_seed_settings s
WHERE existing.organization_id IS NULL
  AND existing.category = s.category
  AND existing.key = s.key;

CREATE TEMP TABLE reqara_seed_templates (
    name text PRIMARY KEY,
    category text NOT NULL,
    description text NOT NULL,
    instructions text NOT NULL,
    settings_json jsonb NOT NULL
) ON COMMIT DROP;

INSERT INTO reqara_seed_templates (name, category, description, instructions, settings_json) VALUES
('New Hire Onboarding (Canada)', 'HR & Recruitment', 'Canadian employee onboarding package covering identity, payroll, tax, emergency contact, policy acknowledgement, and direct deposit.', 'Use for Canadian employee onboarding. Keep SIN collection optional unless payroll setup requires it, and request provincial forms where needed.', '{"industry":"HR","example":"New Hire Onboarding (Canada)","rating":5,"highlight":"Canadian payroll, tax and employee setup in one flow.","country":"Canada","region":"Canada","tags":["employee","new hire","onboarding","canada","payroll"],"searchTerms":["employee","new hire","onboarding","sin","td1","direct deposit","hr"]}'),
('Healthcare Staffing Onboarding (Ontario)', 'HR & Recruitment', 'Ontario healthcare staffing intake with ID, credentials, immunization proof, vulnerable sector check, availability, and placement acknowledgements.', 'Use for Ontario healthcare staffing agencies before placement. Review regulated credentials and screening files before approving a worker.', '{"industry":"HR","example":"Healthcare Staffing Onboarding (Ontario)","rating":5,"highlight":"Built for Ontario healthcare staffing workflows.","country":"Canada","region":"Ontario","tags":["healthcare","staffing","employee","ontario","candidate"],"searchTerms":["psw","nurse","vulnerable sector","immunization","availability","candidate","employee"]}'),
('Independent Contractor Package (Canada)', 'HR & Recruitment', 'Canadian contractor setup with business details, tax status, banking, insurance, agreement acceptance, and compliance documents.', 'Use before engaging an independent contractor in Canada. Collect insurance and tax details without treating the worker as an employee.', '{"industry":"HR","example":"Independent Contractor Package (Canada)","rating":5,"highlight":"Canadian contractor onboarding that mirrors real hiring workflows.","country":"Canada","region":"Canada","tags":["contractor","canada","insurance","tax","banking"],"searchTerms":["contractor","independent contractor","insurance","gst","hst","business number","banking"]}'),
('Tax Season Client Checklist (Canada)', 'Accounting', 'Canadian personal tax document request for T4, T5, RRSP, medical, tuition, rental, business income, receipts, and notes.', 'Use during Canadian tax season. Send early and let clients upload slips, receipts, and special notes securely.', '{"industry":"Accounting","example":"Tax Season Client Checklist (Canada)","rating":5,"highlight":"Everything a Canadian accountant needs in one secure checklist.","country":"Canada","region":"Canada","tags":["tax","accounting","canada","documents"],"searchTerms":["t4","t5","rrsp","medical","tuition","rental income","receipts","tax season"]}'),
('WSIB Contractor Compliance', 'Construction', 'Ontario contractor compliance checklist for WSIB clearance, liability insurance, trade licence, safety certificates, and site acknowledgement.', 'Use before a subcontractor starts Ontario site work. Verify expiry dates before approval.', '{"industry":"Construction","example":"WSIB Contractor Compliance","rating":5,"highlight":"Ontario-ready contractor compliance for site access.","country":"Canada","region":"Ontario","tags":["wsib","contractor","construction","insurance","ontario"],"searchTerms":["wsib","contractor","insurance","trade licence","safety certificate","construction"]}'),
('Property Vendor Compliance (Ontario)', 'Property Management', 'Ontario property vendor onboarding for insurance, WSIB, licences, emergency contacts, after-hours access, and vendor agreement sign-off.', 'Use before approving a vendor for property maintenance, repairs, cleaning, or trades.', '{"industry":"Property Management","example":"Property Vendor Compliance (Ontario)","rating":5,"highlight":"Collect insurance, licences and certificates from property vendors.","country":"Canada","region":"Ontario","tags":["property","vendor","insurance","ontario","compliance"],"searchTerms":["vendor insurance","property insurance","contractor insurance","wsib","licence","property management"]}'),
('Vendor Insurance Renewal', 'Property Management', 'Renewal workflow for vendor liability insurance, expiry dates, WSIB status, trade licence, and updated contact details.', 'Use monthly or quarterly to chase expiring vendor insurance and compliance documents.', '{"industry":"Property Management","example":"Vendor Insurance Renewal","rating":5,"highlight":"A focused renewal checklist for expiring vendor insurance.","country":"Canada","region":"Canada","tags":["insurance","vendor","renewal","property","compliance"],"searchTerms":["insurance","vendor insurance","property insurance","renewal","certificate of insurance","expiry"]}'),
('Mortgage Document Checklist (Canada)', 'Financial Services', 'Canadian mortgage intake for ID, income, employment, assets, liabilities, down payment proof, property details, and consent.', 'Use at the start of a mortgage application. Make income and asset documents required and let applicants explain missing files.', '{"industry":"Financial Services","example":"Mortgage Document Checklist (Canada)","rating":5,"highlight":"Canadian borrower documents in one secure flow.","country":"Canada","region":"Canada","tags":["mortgage","loan","canada","kyc","finance"],"searchTerms":["mortgage","loan","income","employment","assets","liabilities","down payment"]}'),
('Visitor Visa Sponsorship Documents', 'Legal', 'Sponsorship document request for visitor visa support letters, invitation details, ID, employment proof, financial proof, itinerary, and relationship evidence.', 'Use when a sponsor needs to collect supporting documents for a visitor visa application package.', '{"industry":"Legal","example":"Visitor Visa Sponsorship Documents","rating":4,"highlight":"Collect sponsor and travel evidence for visitor visa files.","country":"Canada","region":"Canada","tags":["visa","immigration","sponsorship","documents"],"searchTerms":["visitor visa","sponsorship","invitation letter","passport","financial proof","travel"]}'),
('Home Care Worker Onboarding', 'Healthcare', 'Home care worker onboarding with ID, credentials, vulnerable sector check, immunization, availability, emergency contact, and handbook acknowledgement.', 'Use for home care agencies onboarding PSWs, nurses, caregivers, and support workers.', '{"industry":"Healthcare","example":"Home Care Worker Onboarding","rating":5,"highlight":"Built for agencies onboarding home care staff.","country":"Canada","region":"Canada","tags":["home care","healthcare","employee","onboarding"],"searchTerms":["home care","psw","caregiver","nurse","vulnerable sector","immunization","availability"]}'),
('New Hire Onboarding (U.S.)', 'HR & Recruitment', 'U.S. employee onboarding package covering identity, emergency contact, W-4, I-9 support, direct deposit, handbook acknowledgement, and equipment.', 'Use for U.S. employee onboarding. Do not request more identity data than needed for your payroll and HR process.', '{"industry":"HR","example":"New Hire Onboarding (U.S.)","rating":5,"highlight":"U.S. employee setup with payroll and HR essentials.","country":"United States","region":"United States","tags":["employee","new hire","onboarding","us","payroll"],"searchTerms":["employee","new hire","w-4","i-9","direct deposit","handbook","equipment"]}'),
('Independent Contractor Package (U.S.)', 'HR & Recruitment', 'U.S. independent contractor package with business profile, W-9, banking, insurance, scope acknowledgement, and contract acceptance.', 'Use before engaging a U.S. contractor or freelancer. Keep worker classification and contract records attached.', '{"industry":"HR","example":"Independent Contractor Package (U.S.)","rating":5,"highlight":"U.S. contractor setup with W-9 and insurance collection.","country":"United States","region":"United States","tags":["contractor","us","w-9","insurance","banking"],"searchTerms":["contractor","independent contractor","w-9","insurance","freelancer","banking"]}'),
('W-9 Vendor Registration', 'Accounting', 'U.S. vendor registration with W-9, tax classification, payment details, insurance, primary contact, and compliance acknowledgements.', 'Use before adding a U.S. vendor to accounts payable.', '{"industry":"Accounting","example":"W-9 Vendor Registration","rating":5,"highlight":"U.S. vendor intake for AP and tax reporting.","country":"United States","region":"United States","tags":["vendor","w-9","accounting","us"],"searchTerms":["vendor","w-9","tax","accounts payable","banking","insurance"]}'),
('Loan Application Documents (U.S.)', 'Financial Services', 'U.S. loan application intake for ID, income, employment, assets, liabilities, credit authorization, and supporting documents.', 'Use for loan or financing applications where applicants need to provide proof documents securely.', '{"industry":"Financial Services","example":"Loan Application Documents (U.S.)","rating":5,"highlight":"A practical U.S. borrower document checklist.","country":"United States","region":"United States","tags":["loan","finance","us","documents"],"searchTerms":["loan","income","employment","assets","liabilities","credit authorization","borrower"]}'),
('OSHA Contractor Compliance', 'Construction', 'U.S. contractor compliance checklist for OSHA training, insurance, licences, safety plan, incident history, and site acknowledgement.', 'Use before granting a contractor access to U.S. job sites or facilities.', '{"industry":"Construction","example":"OSHA Contractor Compliance","rating":5,"highlight":"U.S. site compliance with OSHA and insurance documents.","country":"United States","region":"United States","tags":["osha","contractor","construction","insurance","safety"],"searchTerms":["osha","contractor","insurance","safety plan","licence","site induction"]}'),
('Insurance Claim Evidence (U.S.)', 'Insurance', 'U.S. insurance claim evidence request for photos, receipts, police report, videos, witness details, and claimant statement.', 'Use after claim intake to collect evidence while the event is still fresh.', '{"industry":"Insurance","example":"Insurance Claim Evidence (U.S.)","rating":4,"highlight":"Claim evidence collection with photos, receipts and reports.","country":"United States","region":"United States","tags":["insurance","claim","evidence","us"],"searchTerms":["insurance","claim insurance","vehicle insurance","property insurance","photos","receipts","police report"]}'),
('Candidate Onboarding', 'Human Resources & Recruiting', 'Recruiting and staffing onboarding checklist for identity, resume, credentials, availability, emergency contact, consent, and agreement acknowledgement.', 'Use when a candidate is ready for placement or hiring review.', '{"industry":"HR","example":"Candidate Onboarding","rating":5,"highlight":"Used by staffing agencies and recruiters.","country":"Global","region":"Global","pack":"Core","tags":["candidate","employee","onboarding","recruiting"],"searchTerms":["candidate","employee","resume","certifications","availability","background check"]}'),
('New Employee Onboarding', 'Human Resources & Recruiting', 'Employee onboarding package for personal details, tax forms, direct deposit, benefits, emergency contact, handbook, equipment, and photo.', 'Use once an employee accepts an offer and needs to complete HR setup.', '{"industry":"HR","example":"New Employee Onboarding","rating":5,"highlight":"A complete employee setup workflow.","country":"Global","region":"Global","pack":"Core","tags":["employee","new hire","onboarding","hr"],"searchTerms":["employee","new hire","tax forms","direct deposit","benefits","equipment"]}'),
('Independent Contractor Onboarding', 'Human Resources & Recruiting', 'Contractor onboarding for business details, tax information, insurance, banking, certifications, and signed agreement.', 'Use before engaging a contractor or freelancer.', '{"industry":"HR","example":"Independent Contractor Onboarding","rating":5,"highlight":"Collect contractor business, tax and compliance details.","country":"Global","region":"Global","pack":"Core","tags":["contractor","onboarding","insurance","tax"],"searchTerms":["contractor","insurance","banking","tax information","certifications"]}'),
('Employee Offboarding', 'Human Resources & Recruiting', 'Employee exit checklist for equipment return, access card return, final acknowledgement, exit questionnaire, and forwarding information.', 'Use when an employee resigns, is terminated, or finishes a contract.', '{"industry":"HR","example":"Employee Offboarding","rating":4,"highlight":"Close out equipment, access and final HR items.","country":"Global","region":"Global","pack":"Core","tags":["employee","exit","offboarding","equipment"],"searchTerms":["employee","exit","offboarding","equipment return","access card","exit questionnaire"]}'),
('Reference Check', 'Human Resources & Recruiting', 'Reference check workflow for referee details, employment history, performance ratings, and comments.', 'Use during hiring or placement review.', '{"industry":"HR","example":"Reference Check","rating":4,"highlight":"Structured reference collection for recruiters.","country":"Global","region":"Global","pack":"Core","tags":["reference","candidate","recruiting"],"searchTerms":["reference","referee","employment history","performance rating","comments"]}'),
('Personal Tax Document Collection', 'Accounting & Tax', 'Personal tax document request for tax slips, retirement contributions, tuition, medical, rental, investment income, and receipts.', 'Use during tax season to collect documents securely from clients.', '{"industry":"Accounting","example":"Personal Tax Document Collection","rating":5,"highlight":"Everything a tax preparer needs from an individual client.","country":"Global","region":"Global","pack":"Core","tags":["tax","accounting","personal tax","documents"],"searchTerms":["t4","w-2","t5","1099","rrsp","ira","tuition","medical","receipts"]}'),
('Business Tax Package', 'Accounting & Tax', 'Business tax package for financial statements, payroll summaries, receipts, corporate information, and sales tax returns.', 'Use for corporate or small business tax preparation.', '{"industry":"Accounting","example":"Business Tax Package","rating":5,"highlight":"A practical business tax document workflow.","country":"Global","region":"Global","pack":"Core","tags":["tax","business","accounting"],"searchTerms":["business tax","financial statements","payroll","expenses","gst","hst","sales tax"]}'),
('Monthly Bookkeeping Package', 'Accounting & Tax', 'Monthly bookkeeping request for bank statements, credit card statements, payroll, receipts, and outstanding invoices.', 'Use every month to keep bookkeeping packages consistent.', '{"industry":"Accounting","example":"Monthly Bookkeeping Package","rating":5,"highlight":"Monthly records in one repeatable checklist.","country":"Global","region":"Global","pack":"Core","tags":["bookkeeping","accounting","monthly"],"searchTerms":["bank statements","credit card","payroll","receipts","invoices"]}'),
('CRA / IRS Audit Response', 'Accounting & Tax', 'Audit response checklist for requested notices, source documents, receipts, correspondence, and explanatory notes.', 'Use when responding to a tax authority request.', '{"industry":"Accounting","example":"CRA / IRS Audit Response","rating":5,"highlight":"Collect requested audit documents clearly and securely.","country":"Global","region":"Global","pack":"Core","tags":["audit","tax","cra","irs"],"searchTerms":["audit","cra","irs","notices","receipts","supporting documents"]}'),
('Business Incorporation Package', 'Accounting & Tax', 'Business incorporation intake for directors, shareholders, articles, ID, banking, and corporate address.', 'Use when forming or registering a new company.', '{"industry":"Accounting","example":"Business Incorporation Package","rating":4,"highlight":"Start incorporation files with complete owner and company details.","country":"Global","region":"Global","pack":"Core","tags":["incorporation","business","legal","accounting"],"searchTerms":["directors","shareholders","articles","id","banking","corporate address"]}'),
('Vendor Compliance', 'Property Management', 'Vendor compliance checklist for insurance, workers compensation, trade licence, safety certificates, and business registration.', 'Use before approving vendors for work.', '{"industry":"Property Management","example":"Vendor Compliance","rating":5,"highlight":"Collect vendor insurance, licences and certificates.","country":"Global","region":"Global","pack":"Core","tags":["vendor","insurance","compliance","property"],"searchTerms":["insurance","wsib","workers compensation","trade licence","safety certificates"]}'),
('Contractor Registration', 'Property Management', 'Contractor registration workflow for company details, contact, insurance, banking, and certifications.', 'Use to register contractors before assigning work.', '{"industry":"Property Management","example":"Contractor Registration","rating":4,"highlight":"Register contractors with the essentials.","country":"Global","region":"Global","pack":"Core","tags":["contractor","registration","property"],"searchTerms":["contractor","company details","insurance","banking","certifications"]}'),
('Tenant Move-In', 'Property Management', 'Tenant move-in checklist for ID, insurance, emergency contacts, lease acknowledgement, and parking information.', 'Use before or during move-in.', '{"industry":"Property Management","example":"Tenant Move-In","rating":5,"highlight":"Collect move-in documents before keys are handed over.","country":"Global","region":"Global","pack":"Core","tags":["tenant","move in","property"],"searchTerms":["tenant","move-in","id","insurance","lease","parking"]}'),
('Tenant Move-Out', 'Property Management', 'Tenant move-out checklist for photos, damage notes, keys returned, and forwarding address.', 'Use during move-out inspections and deposit review.', '{"industry":"Property Management","example":"Tenant Move-Out","rating":4,"highlight":"Capture photos, damage and key return details.","country":"Global","region":"Global","pack":"Core","tags":["tenant","move out","inspection"],"searchTerms":["move-out","photos","damage","keys","forwarding address"]}'),
('Property Inspection', 'Property Management', 'Property inspection checklist for photos, pass/fail items, maintenance issues, and comments.', 'Use for routine, move-in, move-out, or vendor inspections.', '{"industry":"Property Management","example":"Property Inspection","rating":5,"highlight":"Photos, maintenance issues and inspection comments in one flow.","country":"Global","region":"Global","pack":"Core","tags":["property","inspection","photos"],"searchTerms":["property inspection","photos","pass fail","maintenance","comments"]}'),
('Subcontractor Onboarding', 'Construction', 'Construction subcontractor onboarding for insurance, trade licence, safety certification, equipment certificates, and banking.', 'Use before a subcontractor begins work.', '{"industry":"Construction","example":"Subcontractor Onboarding","rating":5,"highlight":"Construction-ready subcontractor setup.","country":"Global","region":"Global","pack":"Core","tags":["construction","subcontractor","insurance","safety"],"searchTerms":["subcontractor","insurance","trade licence","safety certification","equipment certificates","banking"]}'),
('Site Safety Induction', 'Construction', 'Site induction workflow for emergency contacts, safety acknowledgement, certifications, and PPE confirmation.', 'Use before granting site access.', '{"industry":"Construction","example":"Site Safety Induction","rating":5,"highlight":"Confirm safety readiness before site access.","country":"Global","region":"Global","pack":"Core","tags":["construction","safety","site induction"],"searchTerms":["site induction","emergency contacts","safety acknowledgement","ppe","certifications"]}'),
('Equipment Inspection', 'Construction', 'Equipment inspection checklist for photos, condition, notes, and pass/fail status.', 'Use before, during, or after equipment use.', '{"industry":"Construction","example":"Equipment Inspection","rating":4,"highlight":"Capture equipment condition with photos and pass/fail status.","country":"Global","region":"Global","pack":"Core","tags":["equipment","inspection","construction"],"searchTerms":["equipment","inspection","photos","condition","pass fail"]}'),
('Daily Site Report', 'Construction', 'Daily construction site report for crew, weather, work completed, delays, and photos.', 'Use at the end of each site day.', '{"industry":"Construction","example":"Daily Site Report","rating":4,"highlight":"A practical daily field report.","country":"Global","region":"Global","pack":"Core","tags":["construction","site report","daily"],"searchTerms":["daily site report","crew","weather","work completed","delays","photos"]}'),
('Incident Report', 'Construction', 'Incident report checklist for incident details, witnesses, photos, and corrective actions.', 'Use immediately after a safety, property, or operational incident.', '{"industry":"Construction","example":"Incident Report","rating":5,"highlight":"Capture incident evidence and follow-up actions.","country":"Global","region":"Global","pack":"Core","tags":["incident","safety","construction"],"searchTerms":["incident","witnesses","photos","corrective actions","safety"]}'),
('Insurance Claim Evidence', 'Insurance & Financial Services', 'Claim evidence request for incident details, photos, receipts, police report, and witness statements.', 'Use after claim intake to collect evidence securely.', '{"industry":"Insurance","example":"Insurance Claim Evidence","rating":5,"highlight":"Collect claim evidence without back-and-forth email.","country":"Global","region":"Global","pack":"Core","tags":["insurance","claim","evidence"],"searchTerms":["insurance","claim","photos","receipts","police report","witness"]}'),
('Mortgage Application Documents', 'Insurance & Financial Services', 'Mortgage document request for government ID, income, employment, bank statements, and down payment proof.', 'Use at mortgage application intake.', '{"industry":"Financial Services","example":"Mortgage Application Documents","rating":5,"highlight":"Mortgage application documents in one secure package.","country":"Global","region":"Global","pack":"Core","tags":["mortgage","loan","finance"],"searchTerms":["mortgage","government id","income","employment","bank statements","down payment"]}'),
('Loan Application', 'Insurance & Financial Services', 'Loan application checklist for ID, income, assets, liabilities, and supporting documents.', 'Use for personal, commercial, or specialty loan intake.', '{"industry":"Financial Services","example":"Loan Application","rating":4,"highlight":"Collect borrower proof documents cleanly.","country":"Global","region":"Global","pack":"Core","tags":["loan","finance","application"],"searchTerms":["loan","id","income","assets","liabilities","supporting documents"]}'),
('Client KYC', 'Insurance & Financial Services', 'KYC collection for passport, driver license, proof of address, tax residency, and beneficial ownership.', 'Use for regulated client onboarding and due diligence.', '{"industry":"Financial Services","example":"Client KYC","rating":5,"highlight":"Identity and ownership collection for regulated services.","country":"Global","region":"Global","pack":"Core","tags":["kyc","finance","identity"],"searchTerms":["kyc","passport","driver license","proof of address","tax residency","beneficial ownership"]}'),
('Financial Advisor Client Onboarding', 'Insurance & Financial Services', 'Advisor onboarding checklist for personal information, risk profile, investment objectives, beneficiaries, and existing accounts.', 'Use before opening or reviewing advisory relationships.', '{"industry":"Financial Services","example":"Financial Advisor Client Onboarding","rating":4,"highlight":"Collect investor profile and account details.","country":"Global","region":"Global","pack":"Core","tags":["advisor","wealth","finance","client onboarding"],"searchTerms":["financial advisor","risk profile","investment objectives","beneficiaries","existing accounts"]}'),
('Client Intake', 'Professional Services', 'Professional services intake for contact information, service requested, supporting documents, questions, and consent.', 'Use for agencies, consultants, clinics, or firms starting new work.', '{"industry":"Professional Services","example":"Client Intake","rating":4,"highlight":"A flexible intake for service businesses.","country":"Global","region":"Global","pack":"Core","tags":["client intake","professional services"],"searchTerms":["client intake","contact information","service requested","documents","questions","consent"]}'),
('Legal Matter Intake', 'Professional Services', 'Legal matter intake for parties involved, matter summary, documents, deadlines, and conflict information.', 'Use before opening a legal file or scheduling consultation.', '{"industry":"Legal","example":"Legal Matter Intake","rating":5,"highlight":"Matter facts, parties and deadlines before consultation.","country":"Global","region":"Global","pack":"Core","tags":["legal","client intake","matter"],"searchTerms":["legal matter","parties","matter summary","documents","deadlines","conflict"]}'),
('Immigration Application Package', 'Professional Services', 'Immigration package checklist for passport, visa documents, employment letters, financial documents, and photos.', 'Use for immigration consultants, lawyers, or applicants collecting supporting documents.', '{"industry":"Legal","example":"Immigration Application Package","rating":5,"highlight":"Collect immigration support documents in one place.","country":"Global","region":"Global","pack":"Core","tags":["immigration","visa","documents"],"searchTerms":["immigration","passport","visa documents","employment letters","financial documents","photos"]}'),
('Vendor Registration', 'General Business', 'General vendor registration checklist for company information, banking, tax details, insurance, and contacts.', 'Use before adding a vendor to procurement or finance systems.', '{"industry":"General Business","example":"Vendor Registration","rating":5,"highlight":"A default vendor intake for most businesses.","country":"Global","region":"Global","pack":"Core","tags":["vendor","registration","business"],"searchTerms":["vendor","company information","banking","tax details","insurance","contacts"]}'),
('Secure Document Request', 'General Business', 'Simple secure file request for any documents, notes, and acknowledgement.', 'Use for ad hoc document requests when a full workflow is unnecessary.', '{"industry":"General Business","example":"Secure Document Request","rating":5,"highlight":"The simplest secure document request.","country":"Global","region":"Global","pack":"Core","tags":["document request","files","general"],"searchTerms":["documents","file upload","notes","acknowledgement","secure request"]}'),
('Patient Registration', 'Healthcare', 'Healthcare intake checklist for patient demographics, contact information, insurance, medical history, consent, and ID.', 'Use before the first appointment or registration visit.', '{"industry":"Healthcare","example":"Patient Registration","rating":4,"highlight":"Patient registration documents and consent.","country":"Global","region":"Global","pack":"Healthcare","tags":["patient","healthcare","registration"],"searchTerms":["patient registration","insurance","medical history","consent","id"]}'),
('Referral Intake', 'Healthcare', 'Referral intake checklist for referring provider, reason for referral, clinical notes, test results, and urgency.', 'Use when receiving referrals from providers or partner clinics.', '{"industry":"Healthcare","example":"Referral Intake","rating":4,"highlight":"Collect referral notes and supporting clinical files.","country":"Global","region":"Global","pack":"Healthcare","tags":["referral","healthcare","intake"],"searchTerms":["referral","provider","clinical notes","test results","urgency"]}'),
('Medical Credentialing', 'Healthcare', 'Credentialing checklist for licenses, certifications, insurance, education, work history, and references.', 'Use before approving clinicians or medical providers.', '{"industry":"Healthcare","example":"Medical Credentialing","rating":5,"highlight":"Credentialing documents for healthcare providers.","country":"Global","region":"Global","pack":"Healthcare","tags":["credentialing","healthcare","provider"],"searchTerms":["medical credentialing","license","certifications","insurance","references"]}'),
('Physician Onboarding', 'Healthcare', 'Physician onboarding checklist for credentials, privileges, insurance, tax, banking, orientation, and policy acknowledgement.', 'Use for hospitals, clinics, or healthcare groups onboarding physicians.', '{"industry":"Healthcare","example":"Physician Onboarding","rating":5,"highlight":"Provider onboarding built for clinical organizations.","country":"Global","region":"Global","pack":"Healthcare","tags":["physician","onboarding","healthcare"],"searchTerms":["physician","doctor","privileges","credentials","insurance","orientation"]}'),
('Clinical Trial Enrollment', 'Healthcare', 'Clinical trial enrollment checklist for consent, eligibility, medical records, ID, questionnaires, and visit availability.', 'Use for study teams collecting participant enrollment documents.', '{"industry":"Healthcare","example":"Clinical Trial Enrollment","rating":4,"highlight":"Participant enrollment files for research teams.","country":"Global","region":"Global","pack":"Healthcare","tags":["clinical trial","research","healthcare"],"searchTerms":["clinical trial","consent","eligibility","medical records","questionnaires"]}'),
('Grant Application', 'Government', 'Grant application checklist for applicant details, project plan, budget, supporting documents, declarations, and signature.', 'Use for grant administrators collecting complete submissions.', '{"industry":"Government","example":"Grant Application","rating":4,"highlight":"Structured grant submission package.","country":"Global","region":"Global","pack":"Government","tags":["grant","government","application"],"searchTerms":["grant","project plan","budget","supporting documents","declaration"]}'),
('Permit Request', 'Government', 'Permit request checklist for applicant information, site details, drawings, insurance, approvals, and fee proof.', 'Use for municipal or agency permit intake.', '{"industry":"Government","example":"Permit Request","rating":4,"highlight":"Permit intake with required support files.","country":"Global","region":"Global","pack":"Government","tags":["permit","government","inspection"],"searchTerms":["permit","site details","drawings","insurance","approval"]}'),
('Procurement Submission', 'Government', 'Procurement submission checklist for vendor profile, proposal, pricing, compliance forms, insurance, and references.', 'Use for public procurement or RFP intake.', '{"industry":"Government","example":"Procurement Submission","rating":4,"highlight":"Collect procurement packages consistently.","country":"Global","region":"Global","pack":"Government","tags":["procurement","rfp","government"],"searchTerms":["procurement","proposal","pricing","compliance","insurance","references"]}'),
('Inspection Report', 'Government', 'Inspection report checklist for location, checklist results, photos, violations, corrective actions, and inspector sign-off.', 'Use for public inspections and enforcement follow-up.', '{"industry":"Government","example":"Inspection Report","rating":4,"highlight":"Inspection evidence and corrective actions.","country":"Global","region":"Global","pack":"Government","tags":["inspection","government","report"],"searchTerms":["inspection","photos","violations","corrective actions","sign-off"]}'),
('Licensing Renewal', 'Government', 'Licensing renewal checklist for license details, proof of eligibility, insurance, fees, declarations, and signature.', 'Use when collecting renewal packages for regulated licenses.', '{"industry":"Government","example":"Licensing Renewal","rating":4,"highlight":"Renewal packages without missing forms.","country":"Global","region":"Global","pack":"Government","tags":["licensing","renewal","government"],"searchTerms":["licensing renewal","eligibility","insurance","fees","declaration"]}'),
('Student Registration', 'Education', 'Student registration checklist for student details, guardian contacts, ID, transcripts, medical information, and consent.', 'Use for schools, programs, or training providers registering students.', '{"industry":"Education","example":"Student Registration","rating":4,"highlight":"Student intake documents and consent.","country":"Global","region":"Global","pack":"Education","tags":["student","education","registration"],"searchTerms":["student registration","guardian","transcripts","medical","consent"]}'),
('Scholarship Application', 'Education', 'Scholarship application checklist for applicant profile, transcripts, references, essay, financial information, and declaration.', 'Use to collect scholarship or bursary applications.', '{"industry":"Education","example":"Scholarship Application","rating":4,"highlight":"Scholarship files in one secure application package.","country":"Global","region":"Global","pack":"Education","tags":["scholarship","education","application"],"searchTerms":["scholarship","transcripts","references","essay","financial"]}'),
('Faculty Onboarding', 'Education', 'Faculty onboarding checklist for credentials, tax, banking, course assignment, policy acknowledgement, and ID.', 'Use for schools and universities onboarding faculty.', '{"industry":"Education","example":"Faculty Onboarding","rating":4,"highlight":"Credential and HR setup for educators.","country":"Global","region":"Global","pack":"Education","tags":["faculty","education","onboarding"],"searchTerms":["faculty","teacher","credentials","course assignment","policy"]}'),
('Research Ethics Submission', 'Education', 'Research ethics submission checklist for protocol, consent forms, recruitment materials, risk assessment, and investigator signatures.', 'Use before ethics review or institutional approval.', '{"industry":"Education","example":"Research Ethics Submission","rating":4,"highlight":"Research ethics packages with consent and protocol files.","country":"Global","region":"Global","pack":"Education","tags":["research","ethics","education"],"searchTerms":["research ethics","protocol","consent forms","risk assessment","investigator"]}'),
('Driver Onboarding', 'Logistics', 'Driver onboarding checklist for license, abstract, insurance, emergency contact, banking, safety acknowledgement, and vehicle assignment.', 'Use before assigning drivers to routes or fleet vehicles.', '{"industry":"Logistics","example":"Driver Onboarding","rating":5,"highlight":"Driver documents before dispatch.","country":"Global","region":"Global","pack":"Logistics","tags":["driver","logistics","onboarding"],"searchTerms":["driver","license","abstract","insurance","fleet","safety"]}'),
('Fleet Inspection', 'Logistics', 'Fleet inspection checklist for vehicle photos, mileage, condition, defects, maintenance needs, and pass/fail status.', 'Use for daily, weekly, or handoff vehicle checks.', '{"industry":"Logistics","example":"Fleet Inspection","rating":4,"highlight":"Vehicle condition reports with photos.","country":"Global","region":"Global","pack":"Logistics","tags":["fleet","inspection","vehicle"],"searchTerms":["fleet","vehicle inspection","photos","mileage","defects","maintenance"]}'),
('Delivery Incident Report', 'Logistics', 'Delivery incident report for shipment details, incident description, photos, customer notes, and corrective actions.', 'Use after delivery damage, delay, failed delivery, or safety incidents.', '{"industry":"Logistics","example":"Delivery Incident Report","rating":4,"highlight":"Capture delivery incidents with evidence.","country":"Global","region":"Global","pack":"Logistics","tags":["delivery","incident","logistics"],"searchTerms":["delivery incident","shipment","photos","damage","delay","corrective actions"]}'),
('Supplier Registration', 'Hospitality', 'Hospitality supplier registration for company information, food safety documents, insurance, tax, banking, and contacts.', 'Use before approving suppliers for restaurants, hotels, or venues.', '{"industry":"Hospitality","example":"Supplier Registration","rating":4,"highlight":"Supplier onboarding for hospitality operators.","country":"Global","region":"Global","pack":"Hospitality","tags":["supplier","hospitality","vendor"],"searchTerms":["supplier","food safety","insurance","tax","banking"]}'),
('Seasonal Staff Onboarding', 'Hospitality', 'Seasonal staff onboarding for personal details, availability, tax, banking, uniform size, policy acknowledgement, and emergency contact.', 'Use for restaurants, hotels, venues, and seasonal operations.', '{"industry":"Hospitality","example":"Seasonal Staff Onboarding","rating":4,"highlight":"Fast onboarding for seasonal teams.","country":"Global","region":"Global","pack":"Hospitality","tags":["seasonal staff","hospitality","employee"],"searchTerms":["seasonal staff","availability","tax","banking","uniform","policy"]}'),
('Food Safety Inspection', 'Hospitality', 'Food safety inspection checklist for temperature logs, cleaning checks, photos, issues, corrective actions, and manager sign-off.', 'Use for internal food safety checks or audit prep.', '{"industry":"Hospitality","example":"Food Safety Inspection","rating":4,"highlight":"Operational food safety checks with evidence.","country":"Global","region":"Global","pack":"Hospitality","tags":["food safety","inspection","hospitality"],"searchTerms":["food safety","temperature logs","cleaning","photos","corrective actions"]}');

CREATE TEMP TABLE reqara_seed_requirements (
    template_name text NOT NULL,
    requirement_key text NOT NULL,
    requirement_type smallint NOT NULL,
    label text NOT NULL,
    description text,
    is_required boolean NOT NULL,
    display_order integer NOT NULL,
    configuration_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    validation_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    condition_json jsonb
) ON COMMIT DROP;

INSERT INTO reqara_seed_requirements (template_name, requirement_key, requirement_type, label, description, is_required, display_order, configuration_json) VALUES
('New Hire Onboarding (Canada)', 'legal_name', 1, 'Full legal name', 'Name as it appears on government ID.', TRUE, 10, '{}'),
('New Hire Onboarding (Canada)', 'address', 2, 'Home address', 'Street, city, province, postal code, and country.', TRUE, 20, '{}'),
('New Hire Onboarding (Canada)', 'phone', 11, 'Phone number', 'Best contact number.', TRUE, 30, '{}'),
('New Hire Onboarding (Canada)', 'emergency_contact', 2, 'Emergency contact', 'Name, relationship, phone, and email if available.', TRUE, 40, '{}'),
('New Hire Onboarding (Canada)', 'sin_optional', 1, 'SIN', 'Optional unless payroll setup requires it.', FALSE, 50, '{}'),
('New Hire Onboarding (Canada)', 'td1_forms', 8, 'Federal/provincial TD1 forms', 'Upload completed tax forms.', TRUE, 60, '{"maxFiles":4}'),
('New Hire Onboarding (Canada)', 'direct_deposit', 8, 'Direct deposit document', 'Void cheque or payroll direct deposit form.', TRUE, 70, '{"maxFiles":2}'),
('New Hire Onboarding (Canada)', 'handbook_ack', 5, 'Handbook acknowledgement', 'Confirm the employee handbook has been reviewed.', TRUE, 80, '{"trueLabel":"I have reviewed and acknowledge the employee handbook."}'),

('Healthcare Staffing Onboarding (Ontario)', 'legal_name', 1, 'Full legal name', 'Name as it appears on government ID.', TRUE, 10, '{}'),
('Healthcare Staffing Onboarding (Ontario)', 'government_id', 8, 'Government ID', 'Upload accepted government ID.', TRUE, 20, '{"maxFiles":2}'),
('Healthcare Staffing Onboarding (Ontario)', 'professional_credentials', 8, 'Professional credentials', 'Upload licences, registrations, certificates, or diplomas.', TRUE, 30, '{"maxFiles":10}'),
('Healthcare Staffing Onboarding (Ontario)', 'immunization_records', 8, 'Immunization records', 'Upload immunization or health clearance records required for placement.', TRUE, 40, '{"maxFiles":10}'),
('Healthcare Staffing Onboarding (Ontario)', 'vulnerable_sector_check', 8, 'Vulnerable sector check', 'Upload current screening document.', TRUE, 50, '{"maxFiles":2}'),
('Healthcare Staffing Onboarding (Ontario)', 'availability', 2, 'Availability', 'Share shift availability, preferred locations, and start date.', TRUE, 60, '{}'),
('Healthcare Staffing Onboarding (Ontario)', 'placement_ack', 5, 'Placement acknowledgement', 'Confirm placement requirements have been reviewed.', TRUE, 70, '{"trueLabel":"I understand and accept the placement requirements."}'),

('Independent Contractor Package (Canada)', 'business_profile', 2, 'Business profile', 'Legal business name, address, business number, and primary contact.', TRUE, 10, '{}'),
('Independent Contractor Package (Canada)', 'gst_hst_number', 1, 'GST/HST number', 'Provide GST/HST registration number if applicable.', FALSE, 20, '{}'),
('Independent Contractor Package (Canada)', 'insurance_certificate', 8, 'Insurance certificate', 'Upload liability insurance certificate.', TRUE, 30, '{"maxFiles":3}'),
('Independent Contractor Package (Canada)', 'banking_details', 8, 'Banking details', 'Void cheque or payment banking details.', TRUE, 40, '{"maxFiles":2}'),
('Independent Contractor Package (Canada)', 'tax_forms', 8, 'Tax forms', 'Upload applicable tax forms or business registration documents.', FALSE, 50, '{"maxFiles":5}'),
('Independent Contractor Package (Canada)', 'contract_acceptance', 9, 'Contract acceptance', 'Authorized contractor signature.', TRUE, 60, '{}'),

('Tax Season Client Checklist (Canada)', 't4_slips', 8, 'T4 slips', 'Upload all employment income slips.', TRUE, 10, '{"maxFiles":10}'),
('Tax Season Client Checklist (Canada)', 't5_investment_slips', 8, 'T5 / investment slips', 'Upload investment income slips.', FALSE, 20, '{"maxFiles":10}'),
('Tax Season Client Checklist (Canada)', 'rrsp_receipts', 8, 'RRSP receipts', 'Upload RRSP contribution receipts.', FALSE, 30, '{"maxFiles":10}'),
('Tax Season Client Checklist (Canada)', 'medical_receipts', 8, 'Medical receipts', 'Upload medical receipts or benefits statements.', FALSE, 40, '{"maxFiles":20}'),
('Tax Season Client Checklist (Canada)', 'tuition_slips', 8, 'Tuition slips', 'Upload tuition documents if applicable.', FALSE, 50, '{"maxFiles":10}'),
('Tax Season Client Checklist (Canada)', 'rental_or_business_income', 8, 'Rental or business income', 'Upload statements, invoices, expenses, and receipts.', FALSE, 60, '{"maxFiles":30}'),
('Tax Season Client Checklist (Canada)', 'client_notes', 2, 'Notes for accountant', 'Share changes, questions, or missing documents.', FALSE, 70, '{}'),

('WSIB Contractor Compliance', 'company_profile', 2, 'Company profile', 'Legal company name, contact, and trade category.', TRUE, 10, '{}'),
('WSIB Contractor Compliance', 'wsib_clearance', 8, 'WSIB clearance certificate', 'Upload current WSIB clearance.', TRUE, 20, '{"maxFiles":3}'),
('WSIB Contractor Compliance', 'insurance_certificate', 8, 'Liability insurance certificate', 'Upload current insurance certificate.', TRUE, 30, '{"maxFiles":3}'),
('WSIB Contractor Compliance', 'insurance_expiry', 4, 'Insurance expiry date', 'Expiry date shown on the certificate.', TRUE, 40, '{}'),
('WSIB Contractor Compliance', 'trade_licence', 8, 'Trade licence', 'Upload licence, permit, or registration.', TRUE, 50, '{"maxFiles":5}'),
('WSIB Contractor Compliance', 'safety_certificates', 8, 'Safety certificates', 'Upload required safety training certificates.', TRUE, 60, '{"maxFiles":10}'),
('WSIB Contractor Compliance', 'site_ack', 5, 'Site acknowledgement', 'Confirm site rules and safety requirements have been reviewed.', TRUE, 70, '{"trueLabel":"I confirm the site requirements have been reviewed."}'),

('Property Vendor Compliance (Ontario)', 'vendor_profile', 2, 'Vendor profile', 'Legal name, contact, service category, and service regions.', TRUE, 10, '{}'),
('Property Vendor Compliance (Ontario)', 'insurance_certificate', 8, 'Insurance certificate', 'Upload current certificate of insurance.', TRUE, 20, '{"maxFiles":3}'),
('Property Vendor Compliance (Ontario)', 'wsib_clearance', 8, 'WSIB clearance', 'Upload WSIB clearance if applicable.', FALSE, 30, '{"maxFiles":3}'),
('Property Vendor Compliance (Ontario)', 'trade_licence', 8, 'Trade licence or certificate', 'Upload licences or certificates for regulated work.', FALSE, 40, '{"maxFiles":5}'),
('Property Vendor Compliance (Ontario)', 'emergency_contact', 2, 'Emergency / after-hours contact', 'Name, phone, email, and escalation notes.', TRUE, 50, '{}'),
('Property Vendor Compliance (Ontario)', 'vendor_agreement', 9, 'Vendor agreement sign-off', 'Authorized vendor signature.', TRUE, 60, '{}'),

('Vendor Insurance Renewal', 'vendor_name', 1, 'Vendor name', 'Vendor or contractor legal name.', TRUE, 10, '{}'),
('Vendor Insurance Renewal', 'insurance_certificate', 8, 'Renewed insurance certificate', 'Upload current certificate of insurance.', TRUE, 20, '{"maxFiles":3}'),
('Vendor Insurance Renewal', 'insurance_expiry', 4, 'Insurance expiry date', 'Expiry date on the renewed certificate.', TRUE, 30, '{}'),
('Vendor Insurance Renewal', 'wsib_or_workers_comp', 8, 'WSIB / workers compensation', 'Upload renewed clearance where applicable.', FALSE, 40, '{"maxFiles":3}'),
('Vendor Insurance Renewal', 'licence_update', 8, 'Licence update', 'Upload renewed licence or note no changes.', FALSE, 50, '{"maxFiles":5}'),
('Vendor Insurance Renewal', 'changes_notes', 2, 'Changes or notes', 'Share updated contact, service, or coverage details.', FALSE, 60, '{}'),

('Mortgage Document Checklist (Canada)', 'government_id', 8, 'Government ID', 'Upload front and back of government-issued ID.', TRUE, 10, '{"maxFiles":4}'),
('Mortgage Document Checklist (Canada)', 'income_documents', 8, 'Income documents', 'Pay stubs, T4, NOA, or tax returns.', TRUE, 20, '{"maxFiles":12}'),
('Mortgage Document Checklist (Canada)', 'employment_details', 2, 'Employment details', 'Employer, role, tenure, and contact information.', TRUE, 30, '{}'),
('Mortgage Document Checklist (Canada)', 'asset_statements', 8, 'Asset statements', 'Bank, investment, or down payment proof.', TRUE, 40, '{"maxFiles":12}'),
('Mortgage Document Checklist (Canada)', 'liabilities', 2, 'Liabilities', 'Loans, credit cards, leases, and other obligations.', TRUE, 50, '{}'),
('Mortgage Document Checklist (Canada)', 'property_information', 2, 'Property information', 'Address, purchase price, closing date, and property type if known.', FALSE, 60, '{}'),
('Mortgage Document Checklist (Canada)', 'consent_signature', 9, 'Consent signature', 'Borrower consent to review documents.', TRUE, 70, '{}'),

('Visitor Visa Sponsorship Documents', 'sponsor_id', 8, 'Sponsor ID', 'Upload government ID or status document.', TRUE, 10, '{"maxFiles":3}'),
('Visitor Visa Sponsorship Documents', 'invitation_letter', 8, 'Invitation letter', 'Upload signed invitation/support letter.', TRUE, 20, '{"maxFiles":2}'),
('Visitor Visa Sponsorship Documents', 'employment_proof', 8, 'Employment proof', 'Employment letter or recent pay stubs.', TRUE, 30, '{"maxFiles":6}'),
('Visitor Visa Sponsorship Documents', 'financial_proof', 8, 'Financial proof', 'Bank statements or proof of support.', TRUE, 40, '{"maxFiles":8}'),
('Visitor Visa Sponsorship Documents', 'travel_itinerary', 8, 'Travel itinerary', 'Flights, planned dates, or travel plan.', FALSE, 50, '{"maxFiles":5}'),
('Visitor Visa Sponsorship Documents', 'relationship_evidence', 8, 'Relationship evidence', 'Documents showing relationship to visitor.', FALSE, 60, '{"maxFiles":10}'),
('Visitor Visa Sponsorship Documents', 'sponsor_notes', 2, 'Sponsor notes', 'Anything the representative or reviewer should know.', FALSE, 70, '{}'),

('Home Care Worker Onboarding', 'worker_profile', 2, 'Worker profile', 'Legal name, address, phone, email, and preferred name.', TRUE, 10, '{}'),
('Home Care Worker Onboarding', 'government_id', 8, 'Government ID', 'Upload accepted government ID.', TRUE, 20, '{"maxFiles":2}'),
('Home Care Worker Onboarding', 'credentials', 8, 'Care credentials', 'PSW, nursing, CPR, first aid, or other credentials.', TRUE, 30, '{"maxFiles":10}'),
('Home Care Worker Onboarding', 'screening_check', 8, 'Background / vulnerable sector check', 'Upload current screening document.', TRUE, 40, '{"maxFiles":2}'),
('Home Care Worker Onboarding', 'immunization', 8, 'Immunization records', 'Upload required health records.', FALSE, 50, '{"maxFiles":10}'),
('Home Care Worker Onboarding', 'availability', 2, 'Availability and service area', 'Shifts, service area, travel limits, and start date.', TRUE, 60, '{}'),
('Home Care Worker Onboarding', 'handbook_ack', 5, 'Handbook acknowledgement', 'Confirm care policies and handbook have been reviewed.', TRUE, 70, '{"trueLabel":"I have reviewed the care policies and handbook."}'),

('New Hire Onboarding (U.S.)', 'legal_name', 1, 'Full legal name', 'Name as it appears on government ID.', TRUE, 10, '{}'),
('New Hire Onboarding (U.S.)', 'address', 2, 'Home address', 'Street, city, state, ZIP code, and country.', TRUE, 20, '{}'),
('New Hire Onboarding (U.S.)', 'phone', 11, 'Phone number', 'Best contact number.', TRUE, 30, '{}'),
('New Hire Onboarding (U.S.)', 'emergency_contact', 2, 'Emergency contact', 'Name, relationship, phone, and email if available.', TRUE, 40, '{}'),
('New Hire Onboarding (U.S.)', 'w4_form', 8, 'W-4 form', 'Upload completed W-4.', TRUE, 50, '{"maxFiles":2}'),
('New Hire Onboarding (U.S.)', 'i9_supporting_documents', 8, 'I-9 supporting documents', 'Upload identity/work authorization support documents as requested.', TRUE, 60, '{"maxFiles":4}'),
('New Hire Onboarding (U.S.)', 'direct_deposit', 8, 'Direct deposit document', 'Void check or payroll direct deposit form.', TRUE, 70, '{"maxFiles":2}'),
('New Hire Onboarding (U.S.)', 'handbook_ack', 5, 'Handbook acknowledgement', 'Confirm the employee handbook has been reviewed.', TRUE, 80, '{"trueLabel":"I have reviewed and acknowledge the employee handbook."}'),

('Independent Contractor Package (U.S.)', 'business_profile', 2, 'Business profile', 'Legal business name, address, EIN/SSN handling note, and primary contact.', TRUE, 10, '{}'),
('Independent Contractor Package (U.S.)', 'w9_form', 8, 'W-9 form', 'Upload completed W-9.', TRUE, 20, '{"maxFiles":2}'),
('Independent Contractor Package (U.S.)', 'insurance_certificate', 8, 'Insurance certificate', 'Upload liability insurance certificate.', FALSE, 30, '{"maxFiles":3}'),
('Independent Contractor Package (U.S.)', 'banking_details', 8, 'Banking details', 'Void check or payment banking details.', TRUE, 40, '{"maxFiles":2}'),
('Independent Contractor Package (U.S.)', 'scope_ack', 5, 'Scope acknowledgement', 'Confirm the contractor scope has been reviewed.', TRUE, 50, '{"trueLabel":"I confirm the contractor scope has been reviewed."}'),
('Independent Contractor Package (U.S.)', 'contract_acceptance', 9, 'Contract acceptance', 'Authorized contractor signature.', TRUE, 60, '{}'),

('W-9 Vendor Registration', 'vendor_profile', 2, 'Vendor profile', 'Legal name, address, contact, and business category.', TRUE, 10, '{}'),
('W-9 Vendor Registration', 'w9_form', 8, 'W-9 form', 'Upload completed W-9.', TRUE, 20, '{"maxFiles":2}'),
('W-9 Vendor Registration', 'payment_details', 8, 'Payment details', 'Upload payment instructions or void check.', TRUE, 30, '{"maxFiles":2}'),
('W-9 Vendor Registration', 'insurance_certificate', 8, 'Insurance certificate', 'Upload certificate if required for this vendor type.', FALSE, 40, '{"maxFiles":3}'),
('W-9 Vendor Registration', 'compliance_docs', 8, 'Compliance documents', 'Upload licences, policies, or certificates.', FALSE, 50, '{"maxFiles":10}'),
('W-9 Vendor Registration', 'authorized_signature', 9, 'Authorized signature', 'Vendor sign-off.', TRUE, 60, '{}'),

('Loan Application Documents (U.S.)', 'government_id', 8, 'Government ID', 'Upload government-issued ID.', TRUE, 10, '{"maxFiles":4}'),
('Loan Application Documents (U.S.)', 'income_documents', 8, 'Income documents', 'Pay stubs, W-2, 1099, or tax returns.', TRUE, 20, '{"maxFiles":12}'),
('Loan Application Documents (U.S.)', 'employment_details', 2, 'Employment details', 'Employer, role, tenure, and contact information.', TRUE, 30, '{}'),
('Loan Application Documents (U.S.)', 'asset_statements', 8, 'Asset statements', 'Bank, investment, or collateral statements.', TRUE, 40, '{"maxFiles":12}'),
('Loan Application Documents (U.S.)', 'liabilities', 2, 'Liabilities', 'Loans, credit cards, leases, and other obligations.', TRUE, 50, '{}'),
('Loan Application Documents (U.S.)', 'credit_authorization', 9, 'Credit authorization', 'Applicant authorization signature.', TRUE, 60, '{}'),

('OSHA Contractor Compliance', 'company_profile', 2, 'Company profile', 'Legal company name, contact, trade category, and site contact.', TRUE, 10, '{}'),
('OSHA Contractor Compliance', 'osha_training', 8, 'OSHA training proof', 'Upload OSHA cards or training certificates.', TRUE, 20, '{"maxFiles":10}'),
('OSHA Contractor Compliance', 'insurance_certificate', 8, 'Insurance certificate', 'Upload current certificate of insurance.', TRUE, 30, '{"maxFiles":3}'),
('OSHA Contractor Compliance', 'licences', 8, 'Licences and permits', 'Upload trade licences or permits.', FALSE, 40, '{"maxFiles":5}'),
('OSHA Contractor Compliance', 'safety_plan', 8, 'Safety plan', 'Upload site-specific safety plan or company safety policy.', TRUE, 50, '{"maxFiles":5}'),
('OSHA Contractor Compliance', 'incident_history', 2, 'Incident history', 'Share recent reportable incidents or write none.', TRUE, 60, '{}'),
('OSHA Contractor Compliance', 'site_ack', 5, 'Site acknowledgement', 'Confirm site rules and safety requirements have been reviewed.', TRUE, 70, '{"trueLabel":"I confirm the site requirements have been reviewed."}'),

('Insurance Claim Evidence (U.S.)', 'claim_number', 1, 'Claim number', 'Claim or policy reference number.', TRUE, 10, '{}'),
('Insurance Claim Evidence (U.S.)', 'incident_date', 4, 'Incident date', 'Date the incident occurred.', TRUE, 20, '{}'),
('Insurance Claim Evidence (U.S.)', 'incident_description', 2, 'Incident description', 'Describe what happened and where.', TRUE, 30, '{}'),
('Insurance Claim Evidence (U.S.)', 'photos', 8, 'Photos', 'Upload damage, scene, or evidence photos.', TRUE, 40, '{"maxFiles":25}'),
('Insurance Claim Evidence (U.S.)', 'receipts', 8, 'Receipts and invoices', 'Upload receipts, estimates, or repair invoices.', FALSE, 50, '{"maxFiles":20}'),
('Insurance Claim Evidence (U.S.)', 'police_report', 8, 'Police report', 'Upload police report if filed.', FALSE, 60, '{"maxFiles":3}'),
('Insurance Claim Evidence (U.S.)', 'videos', 8, 'Videos', 'Upload videos if helpful.', FALSE, 70, '{"maxFiles":5}'),
('Insurance Claim Evidence (U.S.)', 'witness_details', 2, 'Witness details', 'Names and contact information for witnesses.', FALSE, 80, '{}');

CREATE TEMP TABLE reqara_seed_requirement_lists (
    template_name text PRIMARY KEY,
    labels text[] NOT NULL
) ON COMMIT DROP;

INSERT INTO reqara_seed_requirement_lists (template_name, labels) VALUES
('Candidate Onboarding', ARRAY['Full legal name','Preferred name','Address','Phone','Email','Government ID','Resume','Certifications','Availability','Emergency contact','Background check consent','Employment agreement acknowledgement']),
('New Employee Onboarding', ARRAY['Personal details','Tax forms','Direct deposit','Benefits enrollment','Emergency contact','Employee handbook acknowledgement','Equipment request','Photo']),
('Independent Contractor Onboarding', ARRAY['Business details','Tax information','Insurance','Banking','Certifications','Signed agreement']),
('Employee Offboarding', ARRAY['Equipment return','Access card return','Final acknowledgement','Exit questionnaire','Forwarding information']),
('Reference Check', ARRAY['Referee details','Employment history','Performance ratings','Comments']),
('Personal Tax Document Collection', ARRAY['T4 / W-2','T5 / 1099','RRSP / IRA','Tuition','Medical','Rental income','Investment income','Receipts']),
('Business Tax Package', ARRAY['Financial statements','Payroll summary','Expense receipts','Corporate information','GST/HST or Sales Tax returns']),
('Monthly Bookkeeping Package', ARRAY['Bank statements','Credit card statements','Payroll','Receipts','Outstanding invoices']),
('CRA / IRS Audit Response', ARRAY['Audit notice','Requested documents','Supporting receipts','Tax authority correspondence','Response notes']),
('Business Incorporation Package', ARRAY['Directors','Shareholders','Articles','Government ID','Banking','Corporate address']),
('Vendor Compliance', ARRAY['Insurance','WSIB / Workers Compensation','Trade licence','Safety certificates','Business registration']),
('Contractor Registration', ARRAY['Company details','Contact','Insurance','Banking','Certifications']),
('Tenant Move-In', ARRAY['Government ID','Tenant insurance','Emergency contacts','Lease acknowledgement','Parking information']),
('Tenant Move-Out', ARRAY['Photos','Damage','Keys returned','Forwarding address']),
('Property Inspection', ARRAY['Photos','Pass/Fail checklist','Maintenance issues','Comments']),
('Subcontractor Onboarding', ARRAY['Insurance','Trade licence','Safety certification','Equipment certificates','Banking']),
('Site Safety Induction', ARRAY['Emergency contacts','Safety acknowledgement','Certifications','PPE confirmation']),
('Equipment Inspection', ARRAY['Photos','Condition','Notes','Pass/Fail']),
('Daily Site Report', ARRAY['Crew','Weather','Work completed','Delays','Photos']),
('Incident Report', ARRAY['Incident details','Witnesses','Photos','Corrective actions']),
('Insurance Claim Evidence', ARRAY['Incident details','Photos','Receipts','Police report','Witness statements']),
('Mortgage Application Documents', ARRAY['Government ID','Income','Employment','Bank statements','Down payment proof']),
('Loan Application', ARRAY['Government ID','Income','Assets','Liabilities','Supporting documents']),
('Client KYC', ARRAY['Passport','Driver licence','Proof of address','Tax residency','Beneficial ownership']),
('Financial Advisor Client Onboarding', ARRAY['Personal information','Risk profile','Investment objectives','Beneficiaries','Existing accounts']),
('Client Intake', ARRAY['Contact information','Service requested','Supporting documents','Questions','Consent']),
('Legal Matter Intake', ARRAY['Parties involved','Matter summary','Documents','Deadlines','Conflict information']),
('Immigration Application Package', ARRAY['Passport','Visa documents','Employment letters','Financial documents','Photos']),
('Vendor Registration', ARRAY['Company information','Banking','Tax details','Insurance','Contacts']),
('Secure Document Request', ARRAY['Any documents','Notes','Acknowledgement']),
('Patient Registration', ARRAY['Patient details','Contact information','Insurance','Medical history','Consent','Government ID']),
('Referral Intake', ARRAY['Referring provider','Reason for referral','Clinical notes','Test results','Urgency']),
('Medical Credentialing', ARRAY['Professional licence','Certifications','Insurance','Education history','Work history','References']),
('Physician Onboarding', ARRAY['Credentials','Privileges','Insurance','Tax information','Banking','Orientation acknowledgement','Policy acknowledgement']),
('Clinical Trial Enrollment', ARRAY['Informed consent','Eligibility questionnaire','Medical records','Government ID','Participant questionnaires','Visit availability']),
('Grant Application', ARRAY['Applicant details','Project plan','Budget','Supporting documents','Declarations','Signature']),
('Permit Request', ARRAY['Applicant information','Site details','Drawings','Insurance','Approvals','Fee proof']),
('Procurement Submission', ARRAY['Vendor profile','Proposal','Pricing','Compliance forms','Insurance','References']),
('Inspection Report', ARRAY['Location','Inspection checklist','Photos','Violations','Corrective actions','Inspector sign-off']),
('Licensing Renewal', ARRAY['License details','Proof of eligibility','Insurance','Fees','Declarations','Signature']),
('Student Registration', ARRAY['Student details','Guardian contacts','Government ID','Transcripts','Medical information','Consent']),
('Scholarship Application', ARRAY['Applicant profile','Transcripts','References','Essay','Financial information','Declaration']),
('Faculty Onboarding', ARRAY['Credentials','Tax information','Banking','Course assignment','Policy acknowledgement','Government ID']),
('Research Ethics Submission', ARRAY['Research protocol','Consent forms','Recruitment materials','Risk assessment','Investigator signatures']),
('Driver Onboarding', ARRAY['Driver licence','Driver abstract','Insurance','Emergency contact','Banking','Safety acknowledgement','Vehicle assignment']),
('Fleet Inspection', ARRAY['Vehicle photos','Mileage','Condition','Defects','Maintenance needs','Pass/Fail status']),
('Delivery Incident Report', ARRAY['Shipment details','Incident description','Photos','Customer notes','Corrective actions']),
('Supplier Registration', ARRAY['Company information','Food safety documents','Insurance','Tax details','Banking','Contacts']),
('Seasonal Staff Onboarding', ARRAY['Personal details','Availability','Tax forms','Banking','Uniform size','Policy acknowledgement','Emergency contact']),
('Food Safety Inspection', ARRAY['Temperature logs','Cleaning checks','Photos','Issues','Corrective actions','Manager sign-off']);

WITH expanded AS (
    SELECT
        list.template_name,
        label,
        ordinality::integer AS ordinal
    FROM reqara_seed_requirement_lists list
    CROSS JOIN LATERAL unnest(list.labels) WITH ORDINALITY AS labels(label, ordinality)
),
normalized AS (
    SELECT
        template_name,
        trim(both '_' from regexp_replace(lower(label), '[^a-z0-9]+', '_', 'g')) AS requirement_key,
        CASE
            WHEN lower(label) LIKE '%phone%' THEN 11
            WHEN lower(label) LIKE '%email%' THEN 10
            WHEN lower(label) LIKE '%date%' OR lower(label) LIKE '%deadline%' THEN 4
            WHEN lower(label) LIKE '%signature%' THEN 9
            WHEN lower(label) LIKE '%acknowledgement%' OR lower(label) LIKE '%consent%' OR lower(label) LIKE '%confirmation%' OR lower(label) LIKE '%declaration%' THEN 5
            WHEN lower(label) LIKE '%photo%' OR lower(label) LIKE '%id%' OR lower(label) LIKE '%passport%' OR lower(label) LIKE '%licence%' OR lower(label) LIKE '%license%' OR lower(label) LIKE '%resume%' OR lower(label) LIKE '%certification%' OR lower(label) LIKE '%forms%' OR lower(label) LIKE '%documents%' OR lower(label) LIKE '%receipts%' OR lower(label) LIKE '%statements%' OR lower(label) LIKE '%report%' OR lower(label) LIKE '%articles%' OR lower(label) LIKE '%drawings%' OR lower(label) LIKE '%proof%' OR lower(label) LIKE '%records%' OR lower(label) LIKE '%letters%' OR lower(label) LIKE '%transcripts%' OR lower(label) LIKE '%essay%' OR lower(label) LIKE '%logs%' THEN 8
            WHEN lower(label) IN ('full legal name','preferred name','address','mileage','weather','condition','urgency') THEN 1
            ELSE 2
        END::smallint AS requirement_type,
        label,
        CASE
            WHEN lower(label) LIKE '%acknowledgement%' OR lower(label) LIKE '%consent%' OR lower(label) LIKE '%confirmation%' OR lower(label) LIKE '%declaration%' THEN 'Confirm this item has been reviewed or accepted.'
            WHEN lower(label) LIKE '%photo%' OR lower(label) LIKE '%documents%' OR lower(label) LIKE '%forms%' OR lower(label) LIKE '%receipts%' OR lower(label) LIKE '%statements%' OR lower(label) LIKE '%report%' OR lower(label) LIKE '%proof%' OR lower(label) LIKE '%records%' THEN 'Upload the requested supporting file or files.'
            ELSE 'Provide the requested information.'
        END AS description,
        CASE
            WHEN lower(label) IN ('preferred name','comments','notes','customer notes','questions') THEN FALSE
            ELSE TRUE
        END AS is_required,
        ordinal * 10 AS display_order,
        CASE
            WHEN lower(label) LIKE '%photo%' THEN '{"maxFiles":20}'::jsonb
            WHEN lower(label) LIKE '%documents%' OR lower(label) LIKE '%receipts%' OR lower(label) LIKE '%statements%' OR lower(label) LIKE '%forms%' THEN '{"maxFiles":15}'::jsonb
            WHEN lower(label) LIKE '%id%' OR lower(label) LIKE '%passport%' OR lower(label) LIKE '%licence%' OR lower(label) LIKE '%license%' THEN '{"maxFiles":4}'::jsonb
            ELSE '{}'::jsonb
        END AS configuration_json
    FROM expanded
)
INSERT INTO reqara_seed_requirements (
    template_name,
    requirement_key,
    requirement_type,
    label,
    description,
    is_required,
    display_order,
    configuration_json
)
SELECT
    template_name,
    requirement_key,
    requirement_type,
    label,
    description,
    is_required,
    display_order,
    configuration_json
FROM normalized generated
WHERE NOT EXISTS (
    SELECT 1
    FROM reqara_seed_requirements existing
    WHERE existing.template_name = generated.template_name
      AND existing.requirement_key = generated.requirement_key
);

INSERT INTO templates (id, organization_id, name, category, description, status, current_version_id, created_by_user_id, created_at, updated_at, deleted_at)
SELECT
    pg_temp.reqara_seed_uuid('reqara-template:' || st.name),
    NULL,
    st.name,
    st.category,
    st.description,
    2,
    NULL,
    NULL,
    now(),
    now(),
    NULL
FROM reqara_seed_templates st
WHERE NOT EXISTS (
    SELECT 1 FROM templates t WHERE t.organization_id IS NULL AND t.name = st.name
);

UPDATE templates t
SET category = st.category,
    description = st.description,
    status = 2,
    updated_at = now(),
    deleted_at = NULL
FROM reqara_seed_templates st
WHERE t.organization_id IS NULL AND t.name = st.name;

INSERT INTO template_versions (id, template_id, version_number, title, instructions, settings_json, published_at, created_by_user_id, created_at)
SELECT
    pg_temp.reqara_seed_uuid('reqara-template-version:' || st.name || ':1'),
    t.id,
    1,
    st.name,
    st.instructions,
    st.settings_json,
    now(),
    NULL,
    now()
FROM reqara_seed_templates st
JOIN templates t ON t.organization_id IS NULL AND t.name = st.name
WHERE NOT EXISTS (
    SELECT 1 FROM template_versions tv WHERE tv.template_id = t.id AND tv.version_number = 1
);

UPDATE template_versions tv
SET title = st.name,
    instructions = st.instructions,
    settings_json = st.settings_json,
    published_at = COALESCE(tv.published_at, now())
FROM templates t
JOIN reqara_seed_templates st ON st.name = t.name
WHERE tv.template_id = t.id
  AND t.organization_id IS NULL
  AND tv.version_number = 1;

DELETE FROM template_requirements tr
USING template_versions tv, templates t, reqara_seed_templates st
WHERE tr.template_version_id = tv.id
  AND tv.template_id = t.id
  AND t.organization_id IS NULL
  AND t.name = st.name
  AND tv.version_number = 1;

INSERT INTO template_requirements (
    id,
    template_version_id,
    key,
    type,
    label,
    description,
    is_required,
    display_order,
    configuration_json,
    validation_json,
    condition_json
)
SELECT
    pg_temp.reqara_seed_uuid('reqara-template-requirement:' || sr.template_name || ':' || sr.requirement_key),
    tv.id,
    sr.requirement_key,
    sr.requirement_type,
    sr.label,
    sr.description,
    sr.is_required,
    sr.display_order,
    sr.configuration_json,
    sr.validation_json,
    sr.condition_json
FROM reqara_seed_requirements sr
JOIN templates t ON t.organization_id IS NULL AND t.name = sr.template_name
JOIN template_versions tv ON tv.template_id = t.id AND tv.version_number = 1;

UPDATE templates t
SET current_version_id = tv.id,
    status = 2,
    updated_at = now()
FROM template_versions tv, reqara_seed_templates st
WHERE tv.template_id = t.id
  AND t.organization_id IS NULL
  AND st.name = t.name
  AND tv.version_number = 1;

SELECT
    t.category,
    t.name,
    tv.settings_json ->> 'country' AS country,
    tv.settings_json ->> 'region' AS region,
    COUNT(tr.id) AS requirement_count
FROM templates t
JOIN template_versions tv ON tv.id = t.current_version_id
LEFT JOIN template_requirements tr ON tr.template_version_id = tv.id
WHERE t.organization_id IS NULL
  AND t.name IN (SELECT name FROM reqara_seed_templates)
GROUP BY t.category, t.name, country, region
ORDER BY country, region, t.category, t.name;

COMMIT;
