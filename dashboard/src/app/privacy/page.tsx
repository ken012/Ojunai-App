import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = {
  title: "Privacy Policy | Ojunai",
  description:
    "Privacy Policy for Ojunai — how we collect, use, and protect your business data.",
};

export default function PrivacyPage() {
  return (
    <div className="min-h-screen bg-slate-50">
      <div className="max-w-3xl mx-auto px-4 py-12">
        <div className="mb-6">
          <Link
            href="/login"
            className="text-sm text-sky-600 hover:underline"
          >
            &larr; Back to Ojunai
          </Link>
        </div>

        <h1 className="text-3xl font-bold text-slate-900 mb-2">
          Privacy Policy
        </h1>
        <p className="text-sm text-slate-500 mb-8">Last updated: April 2026</p>

        <div className="space-y-8 text-slate-700 leading-relaxed">
          <p>
            Ojunai (&ldquo;we&rdquo;, &ldquo;us&rdquo;, or
            &ldquo;our&rdquo;) is committed to protecting your privacy. This
            Privacy Policy explains how we collect, use, store, and share your
            information when you use our platform.
          </p>

          {/* 1. Information We Collect */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 mb-3">
              1. Information We Collect
            </h2>
            <p>We collect the following types of information:</p>
            <ul className="list-disc pl-6 mt-3 space-y-2">
              <li>
                <strong>Business information:</strong> Business name, industry,
                address, and other details you provide during registration.
              </li>
              <li>
                <strong>Contact details:</strong> Your phone number, email
                address, and WhatsApp number.
              </li>
              <li>
                <strong>Transaction data:</strong> Sales records, expenses,
                invoices, inventory data, and other financial information you
                enter or manage through the Service.
              </li>
              <li>
                <strong>WhatsApp messages:</strong> Messages you send to and
                receive from Ojunai via WhatsApp, including text, images,
                and documents shared during conversations.
              </li>
              <li>
                <strong>Usage data:</strong> How you interact with the Service,
                including features used, session duration, and device
                information.
              </li>
            </ul>
          </section>

          {/* 2. How We Use Your Information */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 mb-3">
              2. How We Use Your Information
            </h2>
            <p>We use your information to:</p>
            <ul className="list-disc pl-6 mt-3 space-y-2">
              <li>
                <strong>Deliver the Service:</strong> Process your business
                transactions, generate reports, manage inventory, and provide
                AI-powered business insights.
              </li>
              <li>
                <strong>AI processing:</strong> Your messages and business data
                are processed by our AI to understand your requests, carry out
                tasks, and provide relevant responses and recommendations.
              </li>
              <li>
                <strong>Billing:</strong> Process subscription payments and
                manage your account.
              </li>
              <li>
                <strong>Analytics:</strong> Understand usage patterns to improve
                the Service, fix issues, and develop new features.
              </li>
              <li>
                <strong>Communication:</strong> Send you important updates about
                your account, the Service, or changes to our terms.
              </li>
            </ul>
          </section>

          {/* 3. Payment Data */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 mb-3">
              3. Payment Data
            </h2>
            <p>
              Payments are processed securely through our payment partners:{" "}
              <strong>Paystack</strong> (for Nigeria) and{" "}
              <strong>Flutterwave</strong> (for other markets). We do not store
              your credit or debit card numbers, bank account details, or mobile
              money credentials on our servers. All payment information is
              handled directly by the payment provider in compliance with PCI-DSS
              standards. We only store a reference to your subscription status
              and payment history.
            </p>
          </section>

          {/* 4. WhatsApp Messages */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 mb-3">
              4. WhatsApp Messages
            </h2>
            <p>
              WhatsApp messages you send to Ojunai are processed by our AI
              (powered by Anthropic&rsquo;s Claude) to understand and fulfil your
              requests. Your conversation history is stored to maintain context
              across interactions and provide a seamless experience.
            </p>
            <p className="mt-2">
              Your WhatsApp messages are <strong>not shared</strong> with third
              parties for marketing or advertising purposes. They are only used
              for delivering the Service and improving its quality.
            </p>
          </section>

          {/* 5. Data Sharing */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 mb-3">
              5. Data Sharing
            </h2>
            <p>
              We share your data only with the following service providers, and
              only to the extent necessary to deliver the Service:
            </p>
            <ul className="list-disc pl-6 mt-3 space-y-2">
              <li>
                <strong>Paystack / Flutterwave:</strong> Payment processing.
                They receive the minimum information needed to process your
                payments.
              </li>
              <li>
                <strong>Anthropic (Claude):</strong> AI processing. Your
                messages and relevant business context are sent to
                Anthropic&rsquo;s Claude API to generate responses and carry out
                tasks.
              </li>
              <li>
                <strong>Twilio (WhatsApp):</strong> Message delivery. Twilio
                handles the technical delivery of messages between you and
                Ojunai on WhatsApp.
              </li>
            </ul>
            <p className="mt-3">
              We do not sell your personal or business data to anyone. We may
              disclose information if required by law or to protect our legal
              rights.
            </p>
          </section>

          {/* 6. Data Retention */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 mb-3">
              6. Data Retention
            </h2>
            <p>
              We retain your data for as long as your account is active. If you
              close your account, we will delete your data within 90 days, except
              where we are required to retain it for legal or regulatory reasons.
            </p>
            <p className="mt-2">
              You can export your business data at any time through the
              dashboard. You may also request complete deletion of your data by
              contacting us.
            </p>
          </section>

          {/* 7. Security */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 mb-3">
              7. Security
            </h2>
            <p>
              We take the security of your data seriously and implement
              appropriate measures to protect it:
            </p>
            <ul className="list-disc pl-6 mt-3 space-y-2">
              <li>
                All data is transmitted over <strong>HTTPS</strong> with TLS
                encryption.
              </li>
              <li>
                Sensitive data is encrypted at rest in our databases.
              </li>
              <li>
                We enforce strict access controls so only authorised personnel
                can access user data.
              </li>
              <li>
                We regularly review and update our security practices.
              </li>
            </ul>
            <p className="mt-3">
              While we take every reasonable precaution, no system is completely
              secure. If you suspect any unauthorised access to your account,
              please contact us immediately.
            </p>
          </section>

          {/* 8. Your Rights */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 mb-3">
              8. Your Rights
            </h2>
            <p>
              In line with the Nigeria Data Protection Regulation (NDPR) and
              other applicable data protection laws (including GDPR where
              applicable), you have the right to:
            </p>
            <ul className="list-disc pl-6 mt-3 space-y-2">
              <li>
                <strong>Access:</strong> Request a copy of the personal and
                business data we hold about you.
              </li>
              <li>
                <strong>Correction:</strong> Request that we correct any
                inaccurate or incomplete data.
              </li>
              <li>
                <strong>Deletion:</strong> Request deletion of your data,
                subject to legal retention requirements.
              </li>
              <li>
                <strong>Data portability:</strong> Request your data in a
                structured, machine-readable format so you can transfer it to
                another service.
              </li>
              <li>
                <strong>Objection:</strong> Object to certain types of data
                processing.
              </li>
            </ul>
            <p className="mt-3">
              To exercise any of these rights, contact us at{" "}
              <a
                href="mailto:contact@ojunai.com"
                className="text-sky-600 hover:underline"
              >
                contact@ojunai.com
              </a>
              . We will respond within 30 days.
            </p>
          </section>

          {/* 9. Cookies & Analytics */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 mb-3">
              9. Cookies &amp; Analytics
            </h2>
            <p>
              We use minimal cookies that are essential for the Service to
              function (such as session cookies for authentication). We do not
              use third-party advertising trackers or sell data to ad networks.
            </p>
            <p className="mt-2">
              We may collect anonymous, aggregated analytics to understand how
              the Service is used and to identify areas for improvement. This
              data cannot be used to identify individual users.
            </p>
          </section>

          {/* 10. Changes to This Policy */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 mb-3">
              10. Changes to This Policy
            </h2>
            <p>
              We may update this Privacy Policy from time to time. When we make
              significant changes, we will notify you via WhatsApp or email. The
              &ldquo;Last updated&rdquo; date at the top of this page indicates
              when the policy was last revised. Continued use of the Service
              after changes take effect constitutes your acceptance of the
              updated policy.
            </p>
          </section>

          {/* 11. Contact */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 mb-3">
              11. Contact
            </h2>
            <p>
              If you have questions or concerns about this Privacy Policy or how
              we handle your data, please contact us at{" "}
              <a
                href="mailto:contact@ojunai.com"
                className="text-sky-600 hover:underline"
              >
                contact@ojunai.com
              </a>
              .
            </p>
          </section>
        </div>

        <div className="mt-12 pt-8 border-t border-slate-200 text-sm text-slate-500">
          <p>
            <Link
              href="/terms"
              className="text-sky-600 hover:underline"
            >
              Terms of Service
            </Link>
            {" | "}
            <Link
              href="/login"
              className="text-sky-600 hover:underline"
            >
              Sign In
            </Link>
          </p>
        </div>
      </div>
    </div>
  );
}
