import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = {
  title: "Terms of Service | Ojunai",
  description:
    "Terms of Service for Ojunai — WhatsApp-first AI business management for African SMEs.",
};

export default function TermsPage() {
  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950">
      <div className="max-w-3xl mx-auto px-4 py-12">
        <div className="mb-6">
          <Link
            href="/login"
            className="text-sm text-cyan-600 hover:underline"
          >
            &larr; Back to Ojunai
          </Link>
        </div>

        <h1 className="text-3xl font-bold text-slate-900 dark:text-slate-50 mb-2">
          Terms of Service
        </h1>
        <p className="text-sm text-slate-500 dark:text-slate-400 mb-8">Last updated: April 2026</p>

        <div className="space-y-8 text-slate-700 dark:text-slate-300 leading-relaxed">
          {/* 1. Acceptance of Terms */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              1. Acceptance of Terms
            </h2>
            <p>
              By accessing or using Ojunai (&ldquo;the Service&rdquo;), you
              agree to be bound by these Terms of Service. If you do not agree to
              these terms, please do not use the Service. These terms apply to
              all users, including business owners, employees, and anyone
              accessing the Service on behalf of a business.
            </p>
          </section>

          {/* 2. Service Description */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              2. Service Description
            </h2>
            <p>
              Ojunai is a WhatsApp-first AI business management platform
              designed for African small and medium enterprises (SMEs). The
              Service provides AI-powered tools for bookkeeping, invoicing,
              inventory management, expense tracking, customer management, and
              business insights &mdash; all accessible through WhatsApp and a web
              dashboard.
            </p>
          </section>

          {/* 3. Account Registration */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              3. Account Registration
            </h2>
            <p>
              To use Ojunai, you must register an account by providing
              accurate and complete information about yourself and your business.
              Each account is tied to a single business. You are responsible for
              maintaining the confidentiality of your login credentials and for
              all activities that occur under your account.
            </p>
            <p className="mt-2">
              You must be at least 18 years old and have the legal authority to
              enter into these terms on behalf of your business.
            </p>
          </section>

          {/* 4. Subscription Plans & Billing */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              4. Subscription Plans &amp; Billing
            </h2>
            <p>
              Ojunai offers the following subscription plans:{" "}
              <strong>Starter</strong>, <strong>Shop</strong>,{" "}
              <strong>Pro</strong>, and <strong>Business</strong>. Each plan
              includes different features and usage limits as described on our
              pricing page.
            </p>
            <ul className="list-disc pl-6 mt-3 space-y-2">
              <li>
                <strong>Billing cycles:</strong> Subscriptions are available on
                monthly and annual billing cycles.
              </li>
              <li>
                <strong>Supported currencies:</strong> We accept payments in NGN,
                GHS, USD, GBP, KES, and ZAR.
              </li>
              <li>
                <strong>Payment providers:</strong> Payments are processed
                through Paystack (for Nigeria) and Flutterwave (for other
                markets). We do not store your card details directly.
              </li>
              <li>
                <strong>Auto-renewal:</strong> Card-based subscriptions
                automatically renew at the end of each billing cycle unless you
                cancel before the renewal date.
              </li>
              <li>
                <strong>Manual renewal:</strong> Subscriptions paid via mobile
                money or bank transfer require manual renewal before the
                expiry date to maintain uninterrupted access.
              </li>
              <li>
                <strong>Grace period:</strong> If your subscription expires, you
                have a 3-day grace period to renew before your access is
                restricted.
              </li>
            </ul>
          </section>

          {/* 5. Cancellation Policy */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              5. Cancellation Policy
            </h2>
            <p>
              You may cancel your subscription at any time through the dashboard
              or by contacting us. Upon cancellation, you will retain access to
              your current plan&rsquo;s features until the end of your billing
              period. No partial refunds will be issued for the remaining time in
              a billing cycle.
            </p>
          </section>

          {/* 6. Refund Policy */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              6. Refund Policy
            </h2>
            <p>
              Subscription fees are non-refundable. If you believe a charge was
              made in error or you have a billing dispute, please contact us at{" "}
              <a
                href="mailto:contact@ojunai.com"
                className="text-cyan-600 hover:underline"
              >
                contact@ojunai.com
              </a>
              . Disputed charges will be handled in accordance with the policies
              of the relevant payment provider (Paystack or Flutterwave).
            </p>
          </section>

          {/* 7. Free Trial */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              7. Free Trial
            </h2>
            <p>
              Ojunai may offer a 30-day free trial for eligible
              subscription plans. During the trial, you have full access to the
              plan&rsquo;s features at no cost. At the end of the trial period
              (plus the 3-day grace period), your account will automatically
              downgrade if you have not subscribed to a paid plan. No payment
              information is required to start a free trial.
            </p>
          </section>

          {/* 8. Acceptable Use */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              8. Acceptable Use
            </h2>
            <p>You agree not to use Ojunai to:</p>
            <ul className="list-disc pl-6 mt-3 space-y-2">
              <li>
                Engage in any illegal, fraudulent, or harmful activity.
              </li>
              <li>
                Misuse the WhatsApp integration, including sending unsolicited
                messages, spam, or content that violates WhatsApp&rsquo;s terms
                of service.
              </li>
              <li>
                Scrape, harvest, or extract data from the Service through
                automated means.
              </li>
              <li>
                Attempt to gain unauthorised access to other users&rsquo;
                accounts or our systems.
              </li>
              <li>
                Use the Service in any way that could damage, disable, or impair
                its operation.
              </li>
            </ul>
            <p className="mt-3">
              We reserve the right to suspend or terminate accounts that violate
              these terms without prior notice.
            </p>
          </section>

          {/* 9. Data & Privacy */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              9. Data &amp; Privacy
            </h2>
            <p>
              Your use of Ojunai is also governed by our{" "}
              <Link
                href="/privacy"
                className="text-cyan-600 hover:underline font-medium"
              >
                Privacy Policy
              </Link>
              , which describes how we collect, use, and protect your data.
              Please review the Privacy Policy carefully.
            </p>
          </section>

          {/* 10. Service Modifications */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              10. Service Modifications
            </h2>
            <p>
              Ojunai is continuously evolving. We may update, modify, or
              discontinue features at any time. Changes to pricing will be
              communicated at least 30 days in advance. We may update these Terms
              of Service from time to time; significant changes will be
              communicated to you via WhatsApp or email. Continued use of the
              Service after changes take effect constitutes your acceptance of
              the revised terms.
            </p>
          </section>

          {/* 11. Limitation of Liability */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              11. Limitation of Liability
            </h2>
            <p>
              Ojunai is provided on an &ldquo;as is&rdquo; and &ldquo;as
              available&rdquo; basis. To the fullest extent permitted by law, we
              disclaim all warranties, express or implied, including warranties
              of merchantability, fitness for a particular purpose, and
              non-infringement.
            </p>
            <p className="mt-2">
              In no event shall Ojunai, its owners, employees, or partners
              be liable for any indirect, incidental, special, consequential, or
              punitive damages, or any loss of profits, revenue, data, or
              business opportunities arising from your use of or inability to use
              the Service. Our total liability for any claim related to the
              Service shall not exceed the amount you paid to us in the 12 months
              preceding the claim.
            </p>
          </section>

          {/* 12. Governing Law */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              12. Governing Law
            </h2>
            <p>
              These Terms of Service are governed by and construed in accordance
              with the laws of the Federal Republic of Nigeria. Any disputes
              arising from these terms or your use of the Service shall be
              resolved in the courts of Nigeria, unless otherwise agreed by both
              parties.
            </p>
          </section>

          {/* 13. Contact */}
          <section>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-50 mb-3">
              13. Contact
            </h2>
            <p>
              If you have questions about these Terms of Service, please contact
              us at{" "}
              <a
                href="mailto:contact@ojunai.com"
                className="text-cyan-600 hover:underline"
              >
                contact@ojunai.com
              </a>
              .
            </p>
          </section>
        </div>

        <div className="mt-12 pt-8 border-t border-slate-200 dark:border-slate-800 text-sm text-slate-500 dark:text-slate-400">
          <p>
            <Link
              href="/privacy"
              className="text-cyan-600 hover:underline"
            >
              Privacy Policy
            </Link>
            {" | "}
            <Link
              href="/login"
              className="text-cyan-600 hover:underline"
            >
              Sign In
            </Link>
          </p>
        </div>
      </div>
    </div>
  );
}
